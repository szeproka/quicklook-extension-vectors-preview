# Writes a small PDF-compatible ".ai" test file: white shapes on two artboards, no background.
param([Parameter(Mandatory)][string]$OutFile)

$pages = @(
    "1 1 1 rg 60 60 280 180 re f 0 0 0 rg",                      # white rectangle
    "1 1 1 rg 200 40 m 360 260 l 40 260 l h f 1 1 1 RG 6 w 20 20 m 380 20 l S"  # white triangle + line
)

$objs = New-Object System.Collections.Generic.List[string]
$objs.Add("<< /Type /Catalog /Pages 2 0 R >>")
$kids = (0..($pages.Count - 1) | ForEach-Object { "$(3 + $_ * 2) 0 R" }) -join " "
$objs.Add("<< /Type /Pages /Kids [ $kids ] /Count $($pages.Count) >>")
for ($i = 0; $i -lt $pages.Count; $i++) {
    $content = $pages[$i]
    $objs.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 300] /Contents $(4 + $i * 2) 0 R >>")
    $objs.Add("<< /Length $($content.Length) >>`nstream`n$content`nendstream")
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.Append("%PDF-1.5`n")
$offsets = @()
for ($i = 0; $i -lt $objs.Count; $i++) {
    $offsets += $sb.Length
    [void]$sb.Append("$($i + 1) 0 obj`n$($objs[$i])`nendobj`n")
}
$xref = $sb.Length
[void]$sb.Append("xref`n0 $($objs.Count + 1)`n0000000000 65535 f `n")
foreach ($o in $offsets) { [void]$sb.Append(("{0:D10} 00000 n `n" -f $o)) }
[void]$sb.Append("trailer`n<< /Size $($objs.Count + 1) /Root 1 0 R >>`nstartxref`n$xref`n%%EOF`n")
[IO.File]::WriteAllText($OutFile, $sb.ToString(), [Text.Encoding]::ASCII)
