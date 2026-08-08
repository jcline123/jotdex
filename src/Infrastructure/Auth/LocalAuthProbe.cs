using Jotdex.Core.Auth;
using Jotdex.Infrastructure.Maintenance;

namespace Jotdex.Infrastructure.Auth;

public sealed class LocalAuthProbe : ILocalAuthProbe
{
    private readonly ILocalAuthService _auth;
    public LocalAuthProbe(ILocalAuthService auth) => _auth = auth;
    public bool IsPasswordSet => _auth.IsSetupComplete;
}
