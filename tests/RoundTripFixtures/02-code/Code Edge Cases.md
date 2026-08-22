---
id: 11111111-1111-1111-1111-111111111102b
title: Code Edge Cases
---

# Code edge cases

Tabs, quotes, registry paths, and incomplete examples must round-trip unchanged.

```powershell
	$path = "HKLM:\SOFTWARE\Microsoft\Windows"
	Write-Output 'single-quoted $var'
	"mixed `t tabs and spaces"
```

```json
{
  "name": "partial example",
  "items": [
```

Trailing spaces on purpose:   

```text
backslash path C:\Temp\logs\
dollar $env:USERNAME
backtick `` nested
```
