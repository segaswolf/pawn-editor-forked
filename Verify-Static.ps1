# Verify-Static.ps1
# Chequeos del repositorio que no necesitan el juego ni compilar. Corre en segundos.
# Cada regla viene de un problema real que ya tuvimos o que vimos en otros mods.
#
#   version-sync      modVersion de About.xml = primera versión del CHANGELOG
#   assembly-version  la versión del DLL sale de About.xml, no está escrita a mano en el .csproj
#   ps1-bom           .ps1 con caracteres no ASCII sin BOM: PowerShell 5.1 los lee mal
#   xml-parse         todo XML del mod (About, Defs, Languages, InGameTests) y los .csproj se pueden leer
#   keyed-missing     claves "PawnEditor.*".Translate() del código sin traducción en Languages/English/Keyed
#   keyed-duplicate   la misma clave definida dos veces en Keyed (RimWorld lo reporta como error)
#   absolute-path     rutas personales (la carpeta de usuario o la de repos en el disco C) dentro de archivos versionados
#   conflict-markers  marcas de conflicto de git que quedaron en archivos
#
# Deuda conocida ($KnownDebt): avisa, pero no hace fallar. Al arreglarla, borrar la entrada.
#
# USO:  .\Verify-Static.ps1            (falla con código 1 si alguna regla falla)
#       .\Verify-Static.ps1 -Strict    (la deuda conocida también cuenta como falla)

param([switch]$Strict)

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
$failures = New-Object System.Collections.ArrayList
$known = New-Object System.Collections.ArrayList
$passes = New-Object System.Collections.ArrayList

# Regla + texto que identifica el caso + motivo.
$KnownDebt = @(
    # Vacía: las dos entradas de absolute-path se saldaron el 2026-10-18.
    # Formato: @{ Rule = 'absolute-path'; Match = 'Archivo.cs'; Reason = 'por qué se tolera' }
)

function Test-Known([string]$rule, [string]$message) {
    foreach ($debt in $KnownDebt) {
        if ($debt.Rule -eq $rule -and $message -like "*$($debt.Match)*") { return $debt.Reason }
    }
    return $null
}

function Add-Fail([string]$rule, [string]$message) {
    $reason = Test-Known $rule $message
    if ($reason) { [void]$known.Add("[$rule] $message  (deuda conocida: $reason)") }
    else { [void]$failures.Add("[$rule] $message") }
}

function Add-Pass([string]$rule, [string]$message) { [void]$passes.Add("[$rule] $message") }

function Get-RepoPath([string]$relative) { return Join-Path $repo $relative }

# Archivos versionados en git (o todos, si git no está disponible).
function Get-TrackedFiles {
    try {
        $files = & git -C $repo -c core.quotePath=false ls-files 2>$null
        if ($LASTEXITCODE -eq 0 -and $files) { return $files }
    } catch { }
    return Get-ChildItem $repo -Recurse -File | ForEach-Object { $_.FullName.Substring($repo.Length + 1) }
}

$tracked = @(Get-TrackedFiles)
$textExtensions = @('.cs', '.csproj', '.xml', '.md', '.ps1', '.json', '.props', '.targets', '.txt', '.yml', '.yaml')
$trackedText = @($tracked | Where-Object { $textExtensions -contains [IO.Path]::GetExtension($_).ToLowerInvariant() })

# --- version-sync -------------------------------------------------------------
$aboutPath = Get-RepoPath 'About\About.xml'
[xml]$about = [IO.File]::ReadAllText($aboutPath)
$modVersion = $about.ModMetaData.modVersion
$changelog = [IO.File]::ReadAllText((Get-RepoPath 'CHANGELOG.md'))
$firstEntry = [regex]::Match($changelog, '(?m)^## \[v([0-9][0-9.]*)\]')
if (-not $firstEntry.Success) {
    Add-Fail 'version-sync' 'CHANGELOG.md no tiene ninguna entrada "## [vX.Y.Z]"'
} elseif ($firstEntry.Groups[1].Value -ne $modVersion) {
    Add-Fail 'version-sync' "About.xml dice $modVersion pero la primera entrada del CHANGELOG es v$($firstEntry.Groups[1].Value)"
} else {
    Add-Pass 'version-sync' "v$modVersion en About.xml y CHANGELOG.md"
}

$csprojPath = Get-RepoPath 'Source\PawnEditorForked\PawnEditor.csproj'
# La versión del DLL debe salir de About.xml (target ReadModVersion), no escribirse a mano.
$csprojText = [IO.File]::ReadAllText($csprojPath)
$assemblyVersion = [regex]::Match($csprojText, '<AssemblyVersion>([^<]+)</AssemblyVersion>')
if (-not $assemblyVersion.Success -or $assemblyVersion.Groups[1].Value -ne '$(ModVersion)') {
    Add-Fail 'assembly-version' 'PawnEditor.csproj debe tomar AssemblyVersion de About.xml ($(ModVersion)), no fijarla a mano'
} else {
    Add-Pass 'assembly-version' 'AssemblyVersion sale de About.xml'
}

# --- ps1-bom ------------------------------------------------------------------
$scripts = Get-ChildItem $repo -Filter *.ps1 -File
foreach ($script in $scripts) {
    $bytes = [IO.File]::ReadAllBytes($script.FullName)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $nonAscii = @($bytes | Where-Object { $_ -gt 0x7F }).Count -gt 0
    if ($nonAscii -and -not $hasBom) { Add-Fail 'ps1-bom' "$($script.Name) tiene tildes u otros caracteres no ASCII y no tiene BOM" }
}
if (-not ($failures | Where-Object { $_ -like '`[ps1-bom`]*' })) { Add-Pass 'ps1-bom' "$($scripts.Count) scripts revisados" }

# --- xml-parse ----------------------------------------------------------------
$xmlFolders = @('About', 'Defs', 'Languages', 'InGameTests')
$xmlFiles = @()
foreach ($folder in $xmlFolders) {
    $path = Get-RepoPath $folder
    if (Test-Path $path) { $xmlFiles += Get-ChildItem $path -Recurse -Filter *.xml -File }
}
$xmlFiles += Get-ChildItem (Get-RepoPath 'Source') -Recurse -Filter *.csproj -File
$badXml = 0
foreach ($file in $xmlFiles) {
    try { [void]([xml][IO.File]::ReadAllText($file.FullName)) }
    catch { $badXml++; Add-Fail 'xml-parse' "$($file.FullName.Substring($repo.Length + 1)): $($_.Exception.Message)" }
}
if ($badXml -eq 0) { Add-Pass 'xml-parse' "$($xmlFiles.Count) archivos" }

# --- keyed-missing / keyed-duplicate ------------------------------------------
$keyed = @{}
$keyedFiles = Get-ChildItem (Get-RepoPath 'Languages\English\Keyed') -Filter *.xml -File
foreach ($file in $keyedFiles) {
    [xml]$doc = [IO.File]::ReadAllText($file.FullName)
    foreach ($node in $doc.LanguageData.ChildNodes) {
        if ($node.NodeType -ne 'Element') { continue }
        if ($keyed.ContainsKey($node.Name)) {
            Add-Fail 'keyed-duplicate' "$($node.Name) está en $($keyed[$node.Name]) y en $($file.Name)"
        } else { $keyed[$node.Name] = $file.Name }
    }
}

$used = @{}
$sources = Get-ChildItem (Get-RepoPath 'Source\PawnEditorForked') -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }
foreach ($file in $sources) {
    $text = [IO.File]::ReadAllText($file.FullName)
    foreach ($match in [regex]::Matches($text, '"(PawnEditor\.[A-Za-z0-9_.]+)"\s*\.Translate')) {
        $key = $match.Groups[1].Value
        if (-not $used.ContainsKey($key)) { $used[$key] = $file.Name }
    }
}
$missing = @($used.Keys | Where-Object { -not $keyed.ContainsKey($_) } | Sort-Object)
foreach ($key in $missing) { Add-Fail 'keyed-missing' "$key (usada en $($used[$key])) no está en Languages\English\Keyed" }
if ($missing.Count -eq 0) { Add-Pass 'keyed-missing' "$($used.Count) claves usadas, todas traducidas ($($keyed.Count) definidas)" }

# --- absolute-path ------------------------------------------------------------
$pathHits = 0
foreach ($relative in $trackedText) {
    $full = Get-RepoPath $relative
    if (-not (Test-Path $full)) { continue }
    $lineNumber = 0
    foreach ($line in [IO.File]::ReadAllLines($full)) {
        $lineNumber++
        if ($line -match '[A-Za-z]:\\(Users|Git)\\') {
            $pathHits++
            Add-Fail 'absolute-path' "${relative}:$lineNumber"
        }
    }
}
if ($pathHits -eq 0) { Add-Pass 'absolute-path' "$($trackedText.Count) archivos versionados" }

# --- conflict-markers ---------------------------------------------------------
$conflicts = 0
foreach ($relative in $trackedText) {
    $full = Get-RepoPath $relative
    if (-not (Test-Path $full)) { continue }
    $lineNumber = 0
    foreach ($line in [IO.File]::ReadAllLines($full)) {
        $lineNumber++
        if ($line -match '^(<{7}|>{7})( |$)') { $conflicts++; Add-Fail 'conflict-markers' "${relative}:$lineNumber" }
    }
}
if ($conflicts -eq 0) { Add-Pass 'conflict-markers' 'sin marcas de conflicto' }

# --- hidden-unicode -----------------------------------------------------------
# Caracteres que no se ven pero cambian el código: ancho cero, inversión de dirección (Trojan Source),
# uso privado, "tag" y selectores de variación. GlassWorm (2025-2026) escondía su carga en secuencias de
# selectores U+FE00-FE0F y U+E0100-E01EF dentro de una cadena que se veía vacía. Un U+FE0F suelto tras un
# emoji es normal (no cabe una carga en un byte); dos o más seguidos, o cualquiera del plano suplementario, no.
$invisible = '[\u200B-\u200F\u202A-\u202E\u2060-\u2064\u2066-\u2069\uFEFF\uE000-\uF8FF]'
$selectorRun = '[\uFE00-\uFE0F](?:[\uFE00-\uFE0F]|\uDB40[\uDC00-\uDDEF])'
$supplementaryHidden = '\uDB40[\uDC00-\uDDEF]'
$hiddenPattern = "$invisible|$selectorRun|$supplementaryHidden"
$hiddenHits = 0
foreach ($relative in $trackedText) {
    $full = Get-RepoPath $relative
    if (-not (Test-Path $full)) { continue }
    $lineNumber = 0
    # ReadAllLines quita el BOM inicial; un U+FEFF que quede está en medio del texto.
    foreach ($line in [IO.File]::ReadAllLines($full)) {
        $lineNumber++
        $match = [regex]::Match($line, $hiddenPattern)
        if (-not $match.Success) { continue }
        $hiddenHits++
        $codePoint = [char]::ConvertToUtf32($line, $match.Index)
        Add-Fail 'hidden-unicode' ("${relative}:$lineNumber carácter invisible U+{0:X4}" -f $codePoint)
    }
}
if ($hiddenHits -eq 0) { Add-Pass 'hidden-unicode' "$($trackedText.Count) archivos sin caracteres invisibles" }

# --- dangerous-api ------------------------------------------------------------
# Lo que un mod de editor no necesita y un código malicioso sí: lanzar procesos, red, cargar código desde
# bytes (el equivalente C# del eval), decodificar Base64, llamadas nativas, registro de Windows, compilar en
# caliente. Si alguno hace falta de verdad, va a $KnownDebt con el motivo.
# System.Reflection.Emit a secas no se marca: ahí viven los OpCodes de los transpilers de Harmony. Solo se
# marca lo que crea ensamblados o módulos nuevos en memoria.
$dangerousApi = 'Process\.Start|ProcessStartInfo|WebClient|HttpClient|WebRequest|TcpClient|UdpClient|System\.Net\.Sockets|' +
    'Assembly\.Load(From|File)?\s*\(|AppDomain\.CurrentDomain\.Load|FromBase64String|DllImport|LibraryImport|' +
    'GetDelegateForFunctionPointer|Microsoft\.Win32|Registry\.(CurrentUser|LocalMachine)|CSharpCodeProvider|' +
    'AssemblyBuilder|ModuleBuilder|DefineDynamicAssembly|CSharpScript'
$apiHits = 0
$codeFiles = @($tracked | Where-Object { $_ -like 'Source*' -and [IO.Path]::GetExtension($_) -eq '.cs' })
foreach ($relative in $codeFiles) {
    $full = Get-RepoPath $relative
    if (-not (Test-Path $full)) { continue }
    $lineNumber = 0
    foreach ($line in [IO.File]::ReadAllLines($full)) {
        $lineNumber++
        $match = [regex]::Match($line, $dangerousApi)
        if (-not $match.Success) { continue }
        $apiHits++
        Add-Fail 'dangerous-api' "${relative}:$lineNumber usa '$($match.Value)'"
    }
}
if ($apiHits -eq 0) { Add-Pass 'dangerous-api' "$($codeFiles.Count) archivos C# sin procesos, red ni carga de código" }

# --- msbuild-exec -------------------------------------------------------------
# Compilar también ejecuta: un <Exec> o un <UsingTask> en un .csproj corre comandos al hacer build, antes de
# cualquier prueba. Es el equivalente de los scripts de instalación de npm. Solo se aceptan los targets conocidos.
$allowedTargets = @('ReadModVersion', 'CheckRimWorldPath')
$msbuildFiles = @($tracked | Where-Object { @('.csproj', '.props', '.targets') -contains [IO.Path]::GetExtension($_).ToLowerInvariant() })
$msbuildHits = 0
foreach ($relative in $msbuildFiles) {
    $text = [IO.File]::ReadAllText((Get-RepoPath $relative))
    foreach ($match in [regex]::Matches($text, '<(Exec|UsingTask)\b')) {
        $msbuildHits++
        Add-Fail 'msbuild-exec' "${relative}: <$($match.Groups[1].Value)> ejecuta código al compilar"
    }
    foreach ($match in [regex]::Matches($text, '<Target\s+Name="([^"]+)"')) {
        if ($allowedTargets -contains $match.Groups[1].Value) { continue }
        $msbuildHits++
        Add-Fail 'msbuild-exec' "${relative}: target nuevo '$($match.Groups[1].Value)' (revisar y agregar a `$allowedTargets)"
    }
}
if ($msbuildHits -eq 0) { Add-Pass 'msbuild-exec' "$($msbuildFiles.Count) proyectos sin comandos al compilar" }

# --- binary-files -------------------------------------------------------------
# Un binario no se puede revisar a ojo. Solo se versionan nuestros DLL compilados por Segas.
$allowedBinaries = @('1.5/Assemblies/PawnEditor.dll', '1.6/Assemblies/PawnEditor.dll')
$binaryExtensions = @('.dll', '.exe', '.zip', '.7z', '.rar', '.so', '.dylib', '.bat', '.cmd', '.vbs', '.js', '.msi', '.scr')
$binaries = @($tracked | Where-Object { $binaryExtensions -contains [IO.Path]::GetExtension($_).ToLowerInvariant() })
$unexpected = @($binaries | Where-Object { $allowedBinaries -notcontains ($_ -replace '\\', '/') })
foreach ($relative in $unexpected) { Add-Fail 'binary-files' "$relative no se puede revisar a ojo (solo se versionan nuestros DLL)" }
if ($unexpected.Count -eq 0) { Add-Pass 'binary-files' "solo nuestros DLL ($($binaries.Count) binarios versionados)" }

# --- Resultado ----------------------------------------------------------------
Write-Host '=== Verify-Static ===' -ForegroundColor Cyan
foreach ($pass in $passes) { Write-Host "  [OK] $pass" -ForegroundColor Green }
foreach ($debt in $known) { Write-Host "  [DEUDA] $debt" -ForegroundColor DarkYellow }
foreach ($failure in $failures) { Write-Host "  [FALLA] $failure" -ForegroundColor Red }

$failed = $failures.Count -gt 0 -or ($Strict -and $known.Count -gt 0)
if ($failed) { Write-Host 'Verify-Static: FALLÓ' -ForegroundColor Red; exit 1 }
Write-Host 'Verify-Static: OK' -ForegroundColor Green
exit 0
