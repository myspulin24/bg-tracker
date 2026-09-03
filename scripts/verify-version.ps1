# UTF-8 s BOM: bez něj Windows PowerShell čte skript jako ANSI a české literály
# v porovnáních přestanou sedět. Nemazat.
#
# Hlídá, že verze projektu drží Semantic Versioning 2.0.0 a že k ní existuje záznam
# v changelogu. Bez parametru kontroluje jen repozitář a běží v CI; s -Tag kontroluje
# navíc vydání a umí vypsat popis vydání z changelogu.
#
#   .\scripts\verify-version.ps1
#   .\scripts\verify-version.ps1 -Tag v0.8.0
#   .\scripts\verify-version.ps1 -Tag v0.8.0 -NotesOut notes.md
[CmdletBinding()]
param(
  [string] $Tag,
  [string] $NotesOut
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $root 'Directory.Build.props'
$changelogPath = Join-Path $root 'CHANGELOG.md'

# Oficiální regulární výraz ze specifikace Semantic Versioning 2.0.0.
$semVer = '^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)' +
          '(?:-(?<prerelease>(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)' +
          '(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?' +
          '(?:\+(?<buildmetadata>[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$'

$problems = [System.Collections.Generic.List[string]]::new()
function Fail([string] $message) { $problems.Add($message) }

# --- 1. VersionPrefix v Directory.Build.props ---------------------------------------
if (-not (Test-Path $propsPath)) { throw "Chybí $propsPath." }
# XmlDocument.Load si poradí s BOM i s deklarací kódování; [xml](Get-Content) ne.
$props = New-Object System.Xml.XmlDocument
$props.Load($propsPath)
$prefixNode = $props.SelectSingleNode('//VersionPrefix')
if (-not $prefixNode) {
  Fail 'Directory.Build.props neobsahuje VersionPrefix.'
  $prefix = $null
}
else {
  $prefix = $prefixNode.InnerText.Trim()
  if ($prefix -notmatch $semVer) {
    Fail "VersionPrefix '$prefix' není platná verze podle Semantic Versioning 2.0.0."
  }
  elseif ($Matches['prerelease'] -or $Matches['buildmetadata']) {
    # Předvydání se lepí přes VersionSuffix, build metadata doplňuje CI z commitu.
    Fail "VersionPrefix '$prefix' musí být jen MAJOR.MINOR.PATCH bez přípony."
  }
}

# --- 2. Changelog ------------------------------------------------------------------
if (-not (Test-Path $changelogPath)) { throw "Chybí $changelogPath." }
$changelog = Get-Content $changelogPath -Encoding UTF8
$headings = @()
for ($i = 0; $i -lt $changelog.Count; $i++) {
  $line = $changelog[$i]
  if ($line -match '^##\s+\[(?<name>[^\]]+)\](?:\s+-\s+(?<date>\S+))?\s*$') {
    $headings += [pscustomobject]@{
      Name  = $Matches['name']
      Date  = $Matches['date']
      Line  = $i
    }
  }
}

if ($headings.Count -eq 0) { Fail 'Changelog neobsahuje ani jednu sekci ve tvaru "## [verze] - datum".' }
if ($headings.Count -gt 0 -and $headings[0].Name -ne 'Nevydáno') {
  Fail 'První sekce changelogu musí být "## [Nevydáno]", aby bylo kam psát rozpracované změny.'
}

$released = $headings | Where-Object { $_.Name -ne 'Nevydáno' }
foreach ($heading in $released) {
  if ($heading.Name -notmatch $semVer) {
    Fail "Sekce changelogu '[$($heading.Name)]' není platná verze podle Semantic Versioning 2.0.0."
  }
  if (-not $heading.Date) {
    Fail "Sekce changelogu '[$($heading.Name)]' nemá datum ve tvaru '## [$($heading.Name)] - RRRR-MM-DD'."
  }
  elseif ($heading.Date -notmatch '^\d{4}-\d{2}-\d{2}$') {
    Fail "Sekce changelogu '[$($heading.Name)]' má datum '$($heading.Date)', čekám tvar RRRR-MM-DD."
  }
}

$duplicates = $released | Group-Object Name | Where-Object Count -gt 1
foreach ($duplicate in $duplicates) { Fail "Verze '$($duplicate.Name)' je v changelogu vícekrát." }

# Sestupné pořadí podle precedence ze specifikace: novější verze patří nahoru.
function Compare-SemVer([string] $left, [string] $right) {
  $parse = {
    param($value)
    $null = $value -match $semVer
    [pscustomobject]@{
      Core       = @([int] $Matches['major'], [int] $Matches['minor'], [int] $Matches['patch'])
      PreRelease = $Matches['prerelease']
    }
  }
  $a = & $parse $left
  $b = & $parse $right
  for ($i = 0; $i -lt 3; $i++) {
    if ($a.Core[$i] -ne $b.Core[$i]) { return $a.Core[$i] - $b.Core[$i] }
  }
  # Verze s předvydáním má nižší precedenci než tatáž bez něj.
  if ($a.PreRelease -and -not $b.PreRelease) { return -1 }
  if ($b.PreRelease -and -not $a.PreRelease) { return 1 }
  return [string]::CompareOrdinal($a.PreRelease, $b.PreRelease)
}

for ($i = 1; $i -lt $released.Count; $i++) {
  $newer = $released[$i - 1].Name
  $older = $released[$i].Name
  if ($newer -match $semVer -and $older -match $semVer -and (Compare-SemVer $newer $older) -le 0) {
    Fail "Changelog není v sestupném pořadí: '[$newer]' je nad '[$older]'."
  }
}

# --- 3. Vydání: tag musí sedět na props i na changelog ------------------------------
if ($Tag) {
  if ($Tag -notmatch '^v(?<version>.+)$') {
    Fail "Tag '$Tag' musí začínat na 'v', například v1.2.3."
  }
  else {
    $version = $Matches['version']
    if ($version -notmatch $semVer) {
      Fail "Tag '$Tag' nenese platnou verzi podle Semantic Versioning 2.0.0."
    }
    else {
      if ($Matches['buildmetadata']) {
        Fail "Tag '$Tag' obsahuje build metadata; ta do tagu nepatří, protože se do precedence nepočítají."
      }
      $core = "$($Matches['major']).$($Matches['minor']).$($Matches['patch'])"
      if ($prefix -and $core -ne $prefix) {
        Fail "Tag '$Tag' říká $core, ale VersionPrefix v Directory.Build.props je $prefix. Zvyš verzi a commitni ji před tagováním."
      }

      $section = $headings | Where-Object Name -eq $version | Select-Object -First 1
      if (-not $section) {
        Fail "Changelog nemá sekci '## [$version] - RRRR-MM-DD'. Vydání bez záznamu v changelogu neprojde."
      }
      elseif ($NotesOut) {
        # Popis vydání = tělo sekce až k dalšímu nadpisu verze nebo k odkazům na konci.
        $start = $section.Line + 1
        $end = $changelog.Count - 1
        for ($i = $start; $i -lt $changelog.Count; $i++) {
          if ($changelog[$i] -match '^##\s+\[' -or $changelog[$i] -match '^\[[^\]]+\]:\s') { $end = $i - 1; break }
        }
        $body = ($changelog[$start..$end] -join "`n").Trim()
        if (-not $body) { Fail "Sekce '[$version]' v changelogu je prázdná." }
        else {
          # UTF-8 bez BOM: BOM by se v popisu vydání na GitHubu ukázal jako cizí znak
          # a Set-Content ho ve Windows PowerShellu přidává.
          [System.IO.File]::WriteAllText($NotesOut, $body, (New-Object System.Text.UTF8Encoding($false)))
          Write-Host "Popis vydání zapsán do $NotesOut ($(($body -split "`n").Count) řádků)."
        }
      }
    }
  }
}

# --- Výsledek ----------------------------------------------------------------------
if ($problems.Count -gt 0) {
  Write-Host "Kontrola verzování nasla $($problems.Count) problem(u):" -ForegroundColor Red
  foreach ($problem in $problems) { Write-Host "  - $problem" -ForegroundColor Red }
  exit 1
}

$label = if ($Tag) { "$Tag (VersionPrefix $prefix)" } else { "VersionPrefix $prefix" }
Write-Host "Verzovani v poradku: $label, changelog ma $($released.Count) vydanych sekci." -ForegroundColor Green

# Explicitní nula: bez ní zůstane $LASTEXITCODE u volajícího nenastavený a kontrola
# `if ($LASTEXITCODE -ne 0)` ve workflow by úspěch vyhodnotila jako chybu.
exit 0
