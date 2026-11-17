$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /codepage:65001 /target:winexe /optimize+ /out:"$PSScriptRoot\Claudiometer.exe" `
  /r:System.Windows.Forms.dll /r:System.Drawing.dll `
  /r:"$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF\UIAutomationClient.dll" `
  /r:"$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF\UIAutomationTypes.dll" `
  /r:"$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll" (Get-ChildItem "$PSScriptRoot\*.cs").FullName
if ($LASTEXITCODE -eq 0) { Write-Host "OK -> $PSScriptRoot\Claudiometer.exe" }
