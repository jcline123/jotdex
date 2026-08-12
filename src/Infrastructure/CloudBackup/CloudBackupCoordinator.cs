using System.Collections.Concurrent;
using Jotdex.Core.CloudBackup;
using Jotdex.Infrastructure.Maintenance;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.CloudBackup;

public sealed class CloudBackupCoordinator : ICloudBackupCoordinator
{
    private readonly ICloudBackupSettingsService _settings;
    private readonly ICloudBackupStateStore _state;
    private readonly ICloudBackupArtifactService _artifacts;
    private readonly ICloudBackupHealthService _health;
    private readonly ICloudBackupSnapshotService _snapshot;
    private readonly IMoveKitCryptoService _crypto;
    private readonly IEnumerable<ICloudBackupProvider> _providers;
    private readonly CloudBackupHashService _hashes;
    private readonly ILogger<CloudBackupCoordinator> _logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private readonly ConcurrentDictionary<string, CloudBackupOperation> _operations = new(StringComparer.OrdinalIgnoreCase);
    private CloudBackupOperation? _active;

    public CloudBackupCoordinator(
        ICloudBackupSettingsService settings,
        ICloudBackupStateStore state,
        ICloudBackupArtifactService artifacts,
        ICloudBackupHealthService health,
        ICloudBackupSnapshotService snapshot,
        IMoveKitCryptoService crypto,
        IEnumerable<ICloudBackupProvider> providers,
        CloudBackupHashService hashes,
        ILogger<CloudBackupCoordinator> logger)
    {
        _settings = settings;
        _state = state;
        _artifacts = artifacts;
        _health = health;
        _snapshot = snapshot;
        _crypto = crypto;
        _providers = providers;
        _hashes = hashes;
        _logger = logger;
    }

    public CloudBackupSummary GetSummary()
    {
        var settings = _settings.Get();
        var state = _state.Get();
        var running = _active?.Running == true;
        var health = _health.Calculate(settings, state, running);
        var available = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in _providers)
            available[p.Kind.ToString()] = p.IsConfiguredInBuild;

        return new CloudBackupSummary
        {
            Settings = settings,
            Health = health,
            State = state,
            ActiveOperation = _active,
            ProviderAvailableInBuild = available,
            EncryptionReady = _crypto.IsPasswordProtectionEnabled && _crypto.HasEncryptionKey,
            PasswordRequired = _crypto.IsPasswordProtectionEnabled && !_crypto.HasEncryptionKey
        };
    }

    public CloudBackupHealth GetHealth()
    {
        var settings = _settings.Get();
        var state = _state.Get();
        return _health.Calculate(settings, state, _active?.Running == true);
    }

    public CloudBackupOperation? GetOperation(string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId)) return null;
        _operations.TryGetValue(operationId, out var op);
        return op;
    }

    public Task<CloudBackupOperation> StartRunAsync(
        CloudBackupRunTrigger trigger,
        CloudProviderKind? provider,
        CancellationToken cancellationToken)
    {
        if (!_runLock.Wait(0))
        {
            if (_active is not null)
                return Task.FromResult(_active);
            // Rare race: wait briefly for the active op to register
            if (!_runLock.Wait(TimeSpan.FromSeconds(2), cancellationToken))
            {
                if (_active is not null)
                    return Task.FromResult(_active);
                throw new InvalidOperationException("Cloud backup run lock busy.");
            }
        }

        if (_active?.Running == true)
        {
            _runLock.Release();
            return Task.FromResult(_active);
        }

        var op = new CloudBackupOperation
        {
            OperationId = Guid.NewGuid().ToString("N"),
            RunId = Guid.NewGuid().ToString("N"),
            Trigger = trigger,
            ProviderFilter = provider,
            StartedUtc = DateTimeOffset.UtcNow,
            Running = true,
            Phase = "starting"
        };
        _active = op;
        _operations[op.OperationId] = op;

        var state = _state.Get();
        state.ActiveOperationId = op.OperationId;
        state.LastRunStartedUtc = op.StartedUtc;
        state.LastRunId = op.RunId;
        _state.Save(state);

        // Lock held until ExecuteAsync finally releases it
        _ = Task.Run(() => ExecuteAsync(op, CancellationToken.None), CancellationToken.None);
        return Task.FromResult(op);
    }

    private async Task ExecuteAsync(CloudBackupOperation op, CancellationToken ct)
    {
        string? stagingRunId = op.RunId;
        try
        {
            var settings = _settings.Get();
            if (op.ProviderFilter is CloudProviderKind only)
            {
                var cfg = settings.Providers.FirstOrDefault(p => p.Provider == only);
                if (cfg is null)
                {
                    FailOp(op, $"Unknown provider {only}.");
                    return;
                }

                if (!cfg.Enabled)
                {
                    cfg.Enabled = true;
                    _settings.Save(settings);
                    settings = _settings.Get();
                }
            }

            var targets = ResolveRunTargets(settings, op.ProviderFilter);
            if (targets.Count == 0)
            {
                FailOp(op, op.ProviderFilter is null
                    ? "No enabled cloud backup providers. Connect a provider first."
                    : $"Provider {op.ProviderFilter} is not available to run.");
                return;
            }

            ClearProviderAttempt(targets.Select(t => t.Provider));

            if (!_crypto.IsPasswordProtectionEnabled || !_crypto.HasEncryptionKey)
            {
                RecordEncryptionRequired(settings, targets);
                FailOp(op, "Encryption required for cloud backup.");
                return;
            }

            op.Phase = "creatingArtifacts";
            var artifact = await _artifacts.CreateAsync(op.RunId, settings.IncludePlainVaultZip, passwordForInit: null, ct)
                .ConfigureAwait(false);
            if (!artifact.Success || artifact.Generation is null)
            {
                FailOp(op, artifact.Error ?? "Artifact creation failed.");
                return;
            }

            var generation = artifact.Generation;
            stagingRunId = generation.RunId;

            var providerMap = _providers.ToDictionary(p => p.Kind);
            foreach (var cfg in targets)
            {
                ct.ThrowIfCancellationRequested();
                op.ProviderPhases[cfg.Provider.ToString()] = "starting";
                if (!providerMap.TryGetValue(cfg.Provider, out var provider))
                {
                    UpdateProviderFailure(cfg.Provider, CloudBackupFailureCode.ProviderConfigurationMissing,
                        "Provider adapter not registered.", moveKit: true, vaultZip: false);
                    op.ProviderPhases[cfg.Provider.ToString()] = "failed";
                    continue;
                }

                try
                {
                    await UploadGenerationAsync(provider, cfg, settings, generation, op, ct).ConfigureAwait(false);
                    op.ProviderPhases[cfg.Provider.ToString()] = "done";
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Cloud backup failed for {Provider}", cfg.Provider);
                    var code = ex is CloudBackupProviderException cpe ? cpe.Code : CloudBackupFailureCode.Unknown;
                    UpdateProviderFailure(cfg.Provider, code, Sanitize(ex.Message), moveKit: true, vaultZip: settings.IncludePlainVaultZip);
                    op.ProviderPhases[cfg.Provider.ToString()] = "failed";
                }
            }

            op.Phase = "finished";
            op.Running = false;
            op.FinishedUtc = DateTimeOffset.UtcNow;
            var st = _state.Get();
            st.ActiveOperationId = null;
            st.LastRunFinishedUtc = op.FinishedUtc;
            _state.Save(st);
        }
        catch (OperationCanceledException)
        {
            op.Phase = "cancelled";
            op.Error = "Cancelled";
            op.Running = false;
            op.FinishedUtc = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloud backup run failed");
            FailOp(op, Sanitize(ex.Message));
        }
        finally
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(stagingRunId))
                    _snapshot.DeleteRunStaging(stagingRunId);
            }
            catch { /* ignore */ }

            if (ReferenceEquals(_active, op))
                _active = op.Running ? op : null;
            if (_runLock.CurrentCount == 0)
                _runLock.Release();
        }
    }

    private async Task UploadGenerationAsync(
        ICloudBackupProvider provider,
        CloudProviderSettings cfg,
        CloudBackupSettings settings,
        CloudBackupGeneration generation,
        CloudBackupOperation op,
        CancellationToken ct)
    {
        if (!provider.IsConfiguredInBuild)
            throw CloudBackupProviderException.ConfigurationMissing(provider.Kind);

        op.ProviderPhases[provider.Kind.ToString()] = "auth";
        var account = await provider.GetAccountAsync(ct).ConfigureAwait(false);
        CloudProviderQuota quota;
        try
        {
            quota = await provider.GetQuotaAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Quota probe failed for {Provider}; continuing upload", provider.Kind);
            quota = new CloudProviderQuota();
        }

        op.ProviderPhases[provider.Kind.ToString()] = "ensureRoot";
        var root = await provider.EnsureBackupRootAsync(new CloudBackupContext
        {
            BackupSetId = settings.BackupSetId,
            BackupSetName = settings.BackupSetName,
            ExistingRemoteRootId = cfg.RemoteRootId
        }, ct).ConfigureAwait(false);

        cfg.RemoteRootId = root.RootId;
        cfg.RemoteRootDisplayPath = root.DisplayPath;
        cfg.AccountId = account.AccountId;
        cfg.AccountDisplayName = account.DisplayName;
        cfg.AccountEmail = account.Email;
        SaveProviderSettings(cfg);

        var state = _state.Get();
        var pst = state.Providers.First(p => p.Provider == provider.Kind);
        pst.ConnectionState = CloudConnectionState.Connected;
        pst.QuotaTotalBytes = quota.TotalBytes;
        pst.QuotaUsedBytes = quota.UsedBytes;
        pst.QuotaRemainingBytes = quota.RemainingBytes;
        pst.LastAttemptUtc = DateTimeOffset.UtcNow;
        _state.Save(state);

        var moveOk = false;
        var zipOk = !settings.IncludePlainVaultZip;
        string? lastError = null;

        foreach (var artifact in generation.Artifacts)
        {
            op.ProviderPhases[provider.Kind.ToString()] = "upload:" + artifact.Type;
            try
            {
                var remote = await provider.UploadAsync(new CloudUploadRequest
                {
                    Root = root,
                    FileName = artifact.FileName,
                    LocalPath = artifact.LocalPath,
                    Sha256 = artifact.Sha256,
                    ArtifactType = artifact.Type,
                    RunId = generation.RunId,
                    BackupSetId = generation.BackupSetId
                }, progress: null, ct).ConfigureAwait(false);

                var meta = await provider.GetMetadataAsync(new CloudRemoteFileReference
                {
                    FileId = remote.FileId,
                    PathOrName = remote.FileName
                }, ct).ConfigureAwait(false);

                if (meta is null)
                    throw new CloudBackupProviderException(CloudBackupFailureCode.RemoteFileMissing, "Remote file missing after upload.");
                if (meta.SizeBytes != artifact.SizeBytes)
                    throw new CloudBackupProviderException(CloudBackupFailureCode.RemoteSizeMismatch,
                        $"Remote size {meta.SizeBytes} != local {artifact.SizeBytes}.");

                RecordArtifactSuccess(provider.Kind, artifact, remote, meta);
                if (artifact.Type == CloudArtifactTypes.MoveKit) moveOk = true;
                if (artifact.Type == CloudArtifactTypes.VaultZip) zipOk = true;
            }
            catch (Exception ex)
            {
                lastError = Sanitize(ex.Message);
                var code = ex is CloudBackupProviderException cpe ? cpe.Code : CloudBackupFailureCode.UploadFailed;
                RecordArtifactFailure(provider.Kind, artifact.Type, code, lastError);
                if (artifact.Type == CloudArtifactTypes.MoveKit)
                    throw;
            }
        }

        // Manifest last
        op.ProviderPhases[provider.Kind.ToString()] = "upload:manifest";
        var manifestRemote = await provider.UploadAsync(new CloudUploadRequest
        {
            Root = root,
            FileName = generation.ManifestFileName,
            LocalPath = generation.ManifestPath,
            Sha256 = _hashes.Sha256FileHex(generation.ManifestPath),
            ArtifactType = "manifest",
            RunId = generation.RunId,
            BackupSetId = generation.BackupSetId
        }, null, ct).ConfigureAwait(false);

        var manifestMeta = await provider.GetMetadataAsync(new CloudRemoteFileReference
        {
            FileId = manifestRemote.FileId,
            PathOrName = manifestRemote.FileName
        }, ct).ConfigureAwait(false);
        if (manifestMeta is null || manifestMeta.SizeBytes != new FileInfo(generation.ManifestPath).Length)
            throw new CloudBackupProviderException(CloudBackupFailureCode.RemoteSizeMismatch, "Manifest verification failed.");

        var complete = moveOk && zipOk;
        if (complete)
        {
            await PruneRetentionAsync(provider, root, settings.VersionsToKeep, ct).ConfigureAwait(false);
            state = _state.Get();
            pst = state.Providers.First(p => p.Provider == provider.Kind);
            pst.Health = CloudBackupHealthLevel.Healthy;
            pst.ConsecutiveFailures = 0;
            pst.LastFailureCode = CloudBackupFailureCode.None;
            pst.LastFailureMessage = null;
            pst.LastUploadUtc = DateTimeOffset.UtcNow;
            pst.LastVerifiedUtc = DateTimeOffset.UtcNow;
            pst.NextDueUtc = DateTimeOffset.UtcNow.AddHours(settings.IntervalHours);
            _state.Save(state);
        }
        else if (moveOk)
        {
            // Partial — vault zip failed; already recorded
            state = _state.Get();
            pst = state.Providers.First(p => p.Provider == provider.Kind);
            pst.Health = CloudBackupHealthLevel.Warning;
            pst.LastFailureMessage = lastError ?? "Partial generation: vault ZIP failed.";
            _state.Save(state);
        }
    }

    private async Task PruneRetentionAsync(
        ICloudBackupProvider provider,
        CloudRemoteRoot root,
        int versionsToKeep,
        CancellationToken ct)
    {
        try
        {
            var files = await provider.ListBackupsAsync(root, ct).ConfigureAwait(false);
            var manifests = files
                .Where(f => CloudBackupRemoteNaming.IsGenerationManifestName(f.FileName))
                .OrderByDescending(f => f.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Treat each manifest short-run-id as a generation; keep newest N
            var keepIds = manifests
                .Select(m => CloudBackupRemoteNaming.TryParseShortRunId(m.FileName))
                .Where(id => id is not null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(versionsToKeep)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                var id = CloudBackupRemoteNaming.TryParseShortRunId(file.FileName);
                if (id is null) continue;
                if (keepIds.Contains(id)) continue;
                // Only prune if we still have enough complete generations (manifests)
                if (keepIds.Count < versionsToKeep) continue;
                await provider.DeleteAsync(new CloudRemoteFileReference { FileId = file.FileId, PathOrName = file.FileName }, ct)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Retention prune failed for {Provider}", provider.Kind);
            var state = _state.Get();
            var pst = state.Providers.First(p => p.Provider == provider.Kind);
            pst.LastFailureCode = CloudBackupFailureCode.RetentionFailed;
            pst.LastFailureMessage = Sanitize(ex.Message);
            _state.Save(state);
        }
    }

    private void RecordArtifactSuccess(
        CloudProviderKind kind,
        CloudBackupArtifactDescriptor artifact,
        CloudRemoteFile remote,
        CloudRemoteFile meta)
    {
        var state = _state.Get();
        var pst = state.Providers.First(p => p.Provider == kind);
        var slot = artifact.Type == CloudArtifactTypes.VaultZip ? pst.VaultZip : pst.MoveKit;
        slot.LastAttemptUtc = DateTimeOffset.UtcNow;
        slot.LastUploadUtc = DateTimeOffset.UtcNow;
        slot.LastVerifiedUtc = DateTimeOffset.UtcNow;
        slot.LastFileName = artifact.FileName;
        slot.LastRemoteFileId = remote.FileId;
        slot.LastRemoteSizeBytes = meta.SizeBytes;
        slot.LastSha256 = artifact.Sha256;
        slot.LastFailureCode = CloudBackupFailureCode.None;
        slot.LastFailureMessage = null;

        pst.LastAttemptUtc = DateTimeOffset.UtcNow;
        pst.LastUploadUtc = DateTimeOffset.UtcNow;
        pst.LastVerifiedUtc = DateTimeOffset.UtcNow;
        pst.LastArtifactName = artifact.FileName;
        pst.LastRemoteFileId = remote.FileId;
        pst.LastRemoteSizeBytes = meta.SizeBytes;
        _state.Save(state);
    }

    private void RecordArtifactFailure(CloudProviderKind kind, string artifactType, CloudBackupFailureCode code, string message)
    {
        var state = _state.Get();
        var pst = state.Providers.First(p => p.Provider == kind);
        var slot = artifactType == CloudArtifactTypes.VaultZip ? pst.VaultZip : pst.MoveKit;
        slot.LastAttemptUtc = DateTimeOffset.UtcNow;
        slot.LastFailureCode = code;
        slot.LastFailureMessage = message;
        pst.LastAttemptUtc = DateTimeOffset.UtcNow;
        pst.LastFailureCode = code;
        pst.LastFailureMessage = message;
        pst.ConsecutiveFailures++;
        if (code is CloudBackupFailureCode.AuthenticationRequired or CloudBackupFailureCode.TokenRefreshFailed
            or CloudBackupFailureCode.AuthorizationDenied)
            pst.ConnectionState = CloudConnectionState.ReconnectRequired;
        _state.Save(state);
    }

    private static List<CloudProviderSettings> ResolveRunTargets(
        CloudBackupSettings settings,
        CloudProviderKind? filter)
    {
        if (filter is CloudProviderKind only)
        {
            var cfg = settings.Providers.FirstOrDefault(p => p.Provider == only);
            return cfg is null ? [] : [cfg];
        }

        return settings.Providers.Where(p => p.Enabled).ToList();
    }

    private void ClearProviderAttempt(IEnumerable<CloudProviderKind> providers)
    {
        var state = _state.Get();
        var changed = false;
        foreach (var kind in providers)
        {
            var pst = state.Providers.FirstOrDefault(p => p.Provider == kind);
            if (pst is null) continue;
            pst.LastAttemptUtc = DateTimeOffset.UtcNow;
            pst.LastFailureCode = CloudBackupFailureCode.None;
            pst.LastFailureMessage = null;
            pst.Health = CloudBackupHealthLevel.Running;
            pst.MoveKit.LastFailureCode = CloudBackupFailureCode.None;
            pst.MoveKit.LastFailureMessage = null;
            pst.VaultZip.LastFailureCode = CloudBackupFailureCode.None;
            pst.VaultZip.LastFailureMessage = null;
            changed = true;
        }

        if (changed)
            _state.Save(state);
    }

    private void UpdateProviderFailure(
        CloudProviderKind kind,
        CloudBackupFailureCode code,
        string message,
        bool moveKit,
        bool vaultZip)
    {
        var state = _state.Get();
        var pst = state.Providers.First(p => p.Provider == kind);
        pst.LastAttemptUtc = DateTimeOffset.UtcNow;
        pst.LastFailureCode = code;
        pst.LastFailureMessage = message;
        pst.ConsecutiveFailures++;
        pst.Health = CloudBackupHealthLevel.Error;
        if (moveKit)
        {
            pst.MoveKit.LastAttemptUtc = DateTimeOffset.UtcNow;
            pst.MoveKit.LastFailureCode = code;
            pst.MoveKit.LastFailureMessage = message;
        }
        if (vaultZip)
        {
            pst.VaultZip.LastAttemptUtc = DateTimeOffset.UtcNow;
            pst.VaultZip.LastFailureCode = code;
            pst.VaultZip.LastFailureMessage = message;
        }
        if (code is CloudBackupFailureCode.AuthenticationRequired or CloudBackupFailureCode.TokenRefreshFailed
            or CloudBackupFailureCode.AuthorizationDenied or CloudBackupFailureCode.ProviderConfigurationMissing)
            pst.ConnectionState = code == CloudBackupFailureCode.ProviderConfigurationMissing
                ? CloudConnectionState.ConfigurationUnavailable
                : CloudConnectionState.ReconnectRequired;
        _state.Save(state);
    }

    private void RecordEncryptionRequired(CloudBackupSettings settings, List<CloudProviderSettings> enabled)
    {
        var state = _state.Get();
        foreach (var cfg in enabled)
        {
            var pst = state.Providers.First(p => p.Provider == cfg.Provider);
            pst.LastAttemptUtc = DateTimeOffset.UtcNow;
            pst.LastFailureCode = CloudBackupFailureCode.EncryptionRequired;
            pst.LastFailureMessage = "Cloud backup requires encrypted Move Kits.";
            pst.Health = CloudBackupHealthLevel.Error;
            pst.MoveKit.LastFailureCode = CloudBackupFailureCode.EncryptionRequired;
            pst.MoveKit.LastFailureMessage = pst.LastFailureMessage;
            pst.MoveKit.LastAttemptUtc = DateTimeOffset.UtcNow;
        }
        _state.Save(state);
    }

    private void SaveProviderSettings(CloudProviderSettings cfg)
    {
        var settings = _settings.Get();
        var existing = settings.Providers.FirstOrDefault(p => p.Provider == cfg.Provider);
        if (existing is null)
            settings.Providers.Add(cfg);
        else
        {
            existing.AccountId = cfg.AccountId;
            existing.AccountDisplayName = cfg.AccountDisplayName;
            existing.AccountEmail = cfg.AccountEmail;
            existing.RemoteRootId = cfg.RemoteRootId;
            existing.RemoteRootDisplayPath = cfg.RemoteRootDisplayPath;
            existing.Enabled = cfg.Enabled;
        }
        _settings.Save(settings);
    }

    private void FailOp(CloudBackupOperation op, string error)
    {
        op.Error = error;
        op.Phase = "failed";
        op.Running = false;
        op.FinishedUtc = DateTimeOffset.UtcNow;
        var st = _state.Get();
        st.ActiveOperationId = null;
        st.LastRunFinishedUtc = op.FinishedUtc;
        _state.Save(st);
    }

    private static string Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Unknown error";
        var m = message.Trim();
        if (m.Length > 400) m = m[..400];
        return m;
    }
}
