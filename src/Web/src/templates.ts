export type NoteTemplate = {
  id: string
  name: string
  description: string
  /** Suggested default title in the new-note prompt */
  defaultTitle?: string | ((today: string) => string)
  body: (title: string) => string
}

/** Present in Client network notes so the UI can offer “Add site”. */
export const NETWORK_DOC_MARKER = '<!-- jotdex:network-doc -->'
export const NETWORK_SITES_END = '<!-- jotdex:sites-end -->'

export function isNetworkDoc(markdown: string): boolean {
  return markdown.includes(NETWORK_DOC_MARKER)
}

export function nextNetworkSiteNumber(markdown: string): number {
  const matches = markdown.match(/^## Site\s+(\d+)\b/gim) ?? []
  let max = 0
  for (const m of matches) {
    const n = Number(/(\d+)/.exec(m)?.[1] ?? 0)
    if (n > max) max = n
  }
  return max + 1
}

/** Full site block — tables for recreating the network at one physical location. */
export function networkSiteMarkdown(siteNumber: number, siteLabel = ''): string {
  const name = siteLabel.trim() || `Site ${siteNumber}`
  return `## Site ${siteNumber}: ${name}

| Field | Value |
| --- | --- |
| Site name / code | |
| Street address | |
| On-site contact | |
| Closet / rack / MDF-IDF | |
| ISP | |
| Circuit / service ID | |
| Demarc notes | |

### WAN / public

| Item | Value | Notes |
| --- | --- | --- |
| Public / WAN IP(s) | | CIDR if known |
| ISP gateway | | |
| WAN handoff (fiber / cable / Ethernet) | | |
| Modem / ONT IP or mgmt | | |
| Firewall WAN IP | | |
| Firewall LAN / inside IP | | |

### LAN gateways

| Name / role | IP | Subnet / mask | Device | Notes |
| --- | --- | --- | --- | --- |
| Default LAN gateway | | | | |
| | | | | |
| | | | | |

### VLANs & subnets

| VLAN ID | Name | Subnet / CIDR | Gateway IP | DHCP scope | Purpose |
| --- | --- | --- | --- | --- | --- |
| | Data | | | | |
| | Voice | | | | |
| | Servers | | | | |
| | Guest | | | | |
| | Cameras | | | | |
| | Mgmt | | | | |
| | | | | | |

### Static routes

| Destination / prefix | Next hop | Device | Metric / AD | Notes |
| --- | --- | --- | --- | --- |
| | | | | |
| | | | | |
| | | | | |

### DNS

| Role | IP / hostname | Notes |
| --- | --- | --- |
| Primary internal | | |
| Secondary internal | | |
| Forwarders / public | | |

### DHCP

| Scope name | Server | Range start–end | Mask | Gateway option | DNS options | Reservations / exclusions |
| --- | --- | --- | --- | --- | --- | --- |
| | | | | | | |
| | | | | | | |

### Static / reserved hosts

| Hostname / device | IP | VLAN | MAC (if needed) | Purpose |
| --- | --- | --- | --- | --- |
| DC / AD | | | | |
| File server | | | | |
| DHCP / DNS host | | | | |
| Printer / MFP | | | | |
| NVR | | | | |
| AP controller | | | | |
| | | | | |
| | | | | |

### Switching & Wi‑Fi

| Device | Mgmt IP | Model | Uplink / stack | Notes |
| --- | --- | --- | --- | --- |
| Core / L3 switch | | | | |
| Access switch | | | | |
| Wireless controller / APs | | | | |

| SSID | VLAN | Auth | Notes |
| --- | --- | --- | --- |
| | | | |
| | | | |

### NAT / port forwards (this site)

| External IP:port | Internal IP:port | Proto | Purpose |
| --- | --- | --- | --- |
| | | | |
| | | | |

### Site-to-site / remote VPN (this site)

| Peer / name | Local networks | Remote networks | Type | Notes |
| --- | --- | --- | --- | --- |
| | | | | |

### Site notes / gotchas

-

`
}

export const NOTE_TEMPLATES: NoteTemplate[] = [
  {
    id: 'blank',
    name: 'Blank',
    description: 'Title only',
    body: (title) => `# ${title}\n\n`,
  },
  {
    id: 'network',
    name: 'Client network',
    description: 'IP tables by site — use Add site for more locations',
    defaultTitle: 'Network — Client Name',
    body: (title) => `# ${title}

${NETWORK_DOC_MARKER}

**Documented:** ${today()}
**Client:**
**Primary contact:**

## Snapshot

| Item | Value |
| --- | --- |
| Domain / workgroup | |
| M365 / forest | |
| Who manages it | |
| Related diagram | |

${networkSiteMarkdown(1, 'Primary')}
${NETWORK_SITES_END}

## Org-wide notes (optional)

| Item | Value |
| --- | --- |
| Shared AD / DNS across sites | |
| Shared firewall / SD-WAN | |
| Credential vault location | |

## Replacement checklist

- [ ] Export firewall / switch configs
- [ ] Capture WAN handoff photos / labels
- [ ] Confirm every VLAN gateway and DHCP scope above
- [ ] Confirm static hosts and routes
- [ ] Test internet, AD, VPN, phones, cameras after change
- [ ] Update this note with final values
`,
  },
  {
    id: 'server',
    name: 'Server build',
    description: 'Hostname, roles, disks, backups',
    defaultTitle: 'Server — Hostname',
    body: (title) => `# ${title}

**Documented:** ${today()}
**Client / site:**
**Status:** (planned / live / decommission)

## Identity

| Field | Value |
| --- | --- |
| Hostname | |
| Domain | |
| OS / build | |
| Hypervisor / host | |
| IP (primary) | |
| Other IPs | |
| Mgmt URL | |

## Roles & workloads

-

## Hardware / VM specs

| CPU | RAM | Disks | NIC |
| --- | --- | --- | --- |
| | | | |

## Disks & paths

| Drive / mount | Size | Purpose | Backup? |
| --- | --- | --- | --- |
| | | | |

## Services / ports

| Service | Port | Notes |
| --- | --- | --- |
| | | |

## Backup & recovery

| Job | Product | Schedule | Retention | Last tested restore |
| --- | --- | --- | --- | --- |
| | | | | |

## Notes

`,
  },
  {
    id: 'client-overview',
    name: 'Client / site overview',
    description: 'MSP-style client cheat sheet',
    defaultTitle: 'Client — Name',
    body: (title) => `# ${title}

**Documented:** ${today()}
**Account owner:**
**Primary contact:**

## Snapshot

| Item | Value |
| --- | --- |
| Users / seats | |
| Locations | |
| Domain / M365 tenant | |
| RMM / PSA | |

## Key systems

| System | Link / note |
| --- | --- |
| Network | |
| Servers | |
| Firewall | |
| Backup | |
| Email / M365 | |
| Phones | |
| Line-of-business | |

## Vendors

| Vendor | Account # | Portal / phone | Notes |
| --- | --- | --- | --- |
| ISP | | | |
| Microsoft | | | |

## Known quirks

-
`,
  },
  {
    id: 'cutover',
    name: 'Network / firewall cutover',
    description: 'Replace edge or switch — checklist',
    defaultTitle: 'Cutover — Client / site',
    body: (title) => `# ${title}

**Date planned:**
**Client / site:**
**Window:**
**Rollback plan:**

## Pre-work

- [ ] Config backup (firewall / switches)
- [ ] DHCP reservations export
- [ ] WAN handoff photos / labels
- [ ] New gear staged
- [ ] Client notified

## During

- [ ] Move WAN / LAN
- [ ] Apply config
- [ ] Verify DHCP / DNS / gateway
- [ ] Smoke-test internet, AD, VPN, phones, cameras

## Post

- [ ] Update network documentation
- [ ] Update monitoring
- [ ] Client sign-off

## Final IPs

| Role | Old | New |
| --- | --- | --- |
| WAN | | |
| LAN gateway | | |
| DHCP | | |
| DNS | | |
`,
  },
  {
    id: 'firewall',
    name: 'Firewall / VPN',
    description: 'Rules, NAT, VPN tunnels',
    defaultTitle: 'Firewall — Client / site',
    body: (title) => `# ${title}

**Documented:** ${today()}
**Device / firmware:**
**Mgmt IP / URL:**

## Interfaces

| Name | Zone | IP / subnet | Notes |
| --- | --- | --- | --- |
| | | | |

## Site-to-site VPN

| Peer | Local nets | Remote nets | Notes |
| --- | --- | --- | --- |
| | | | |

## Remote access VPN

| Type | Auth | Notes |
| --- | --- | --- |
| | | |

## NAT / forwards

| External | Internal | Proto | Purpose |
| --- | --- | --- | --- |
| | | | |

## Notable rules

| Name | Source | Dest | Service | Why |
| --- | --- | --- | --- | --- |
| | | | | |
`,
  },
  {
    id: 'm365',
    name: 'Microsoft 365 / tenant',
    description: 'Tenant ID, DNS, sync, apps',
    defaultTitle: 'M365 — Client',
    body: (title) => `# ${title}

**Documented:** ${today()}
**Tenant name / ID:**
**Primary domain:**

## Domains & DNS

| Domain | MX / mail | Autodiscover | SPF / DKIM / DMARC |
| --- | --- | --- | --- |
| | | | |

## Identity

| Item | Value |
| --- | --- |
| Sync type | |
| Sync server | |
| MFA / CA | |

## Licenses

| SKU | Count | Notes |
| --- | --- | --- |
| | | |

## Backup

| What | Product | Notes |
| --- | --- | --- |
| | | |

## Gotchas

-
`,
  },
  {
    id: 'backup',
    name: 'Backup & DR',
    description: 'Jobs, retention, restore tests',
    defaultTitle: 'Backup — Client',
    body: (title) => `# ${title}

**Documented:** ${today()}
**Product:**

## Jobs

| Protected item | Schedule | Retention | Destination | Notes |
| --- | --- | --- | --- | --- |
| | | | | |

## Destinations

| Target | Path / URL | Offsite? |
| --- | --- | --- |
| | | |

## Restore tests

| Date | What | Result |
| --- | --- | --- |
| | | |
`,
  },
  {
    id: 'install',
    name: 'Install / project notes',
    description: 'On-site install log',
    defaultTitle: 'Install — Client / job',
    body: (title) => `# ${title}

**Date:** ${today()}
**Client / site:**
**Techs:**

## Scope

-

## Gear / serials

| Item | Model | Serial | Location |
| --- | --- | --- | --- |
| | | | |

## Work performed

1.

## Follow-ups

- [ ]
`,
  },
  {
    id: 'runbook',
    name: 'Runbook / SOP',
    description: 'Repeatable procedure',
    defaultTitle: 'Runbook — Task name',
    body: (title) => `# ${title}

**Last reviewed:** ${today()}
**Systems:**
**Risk / downtime:**

## When to use

-

## Steps

1.
2.
3.

## Verify

-

## Rollback

-
`,
  },
  {
    id: 'vendor',
    name: 'Vendor / circuit',
    description: 'ISP, support, account numbers',
    defaultTitle: 'Vendor — Name',
    body: (title) => `# ${title}

**Documented:** ${today()}
**Client:**
**Vendor:**

## Contacts

| Type | Phone / portal | Account # |
| --- | --- | --- |
| Support | | |
| NOC | | |

## Service

| Field | Value |
| --- | --- |
| Circuit ID | |
| Bandwidth | |
| WAN handoff | |
| Static IPs | |
| Gateway | |
| DNS | |
`,
  },
  {
    id: 'meeting',
    name: 'Meeting',
    description: 'Attendees, notes, actions',
    body: (title) =>
      `# ${title}\n\n**Date:** ${today()}\n\n## Attendees\n\n- \n\n## Notes\n\n\n## Action items\n\n- [ ] \n`,
  },
  {
    id: 'howto',
    name: 'How-to',
    description: 'Steps + troubleshooting',
    body: (title) =>
      `# ${title}\n\n## Goal\n\n\n## Steps\n\n1. \n2. \n3. \n\n## Notes\n\n\n## Troubleshooting\n\n`,
  },
  {
    id: 'incident',
    name: 'Incident / ticket',
    description: 'Symptoms, fix, follow-up',
    body: (title) =>
      `# ${title}\n\n**Date:** ${today()}\n\n## Symptoms\n\n\n## Cause\n\n\n## Fix\n\n\n## Follow-up\n\n- [ ] \n`,
  },
  {
    id: 'daily',
    name: 'Daily note',
    description: 'Quick day log',
    defaultTitle: () => today(),
    body: (title) => `# ${title}\n\n## Focus\n\n- [ ] \n\n## Notes\n\n\n## Tomorrow\n\n- [ ] \n`,
  },
]

function today(): string {
  const d = new Date()
  return d.toISOString().slice(0, 10)
}
