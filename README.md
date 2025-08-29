Ralen is an easy-to-use and easy-to-install environment. To test it for yourself, install the equivalent binary and run it. Or, simply use the following commands to automatically install and configure the path.

# Linux
```
mkdir -p ~/.ralen && curl -L https://github.com/serene1491/Ralen/releases/latest/download/ralenLinux64 -o ~/.ralen/Ralen && chmod +x ~/.ralen/Ralen && ~/.ralen/Ralen configure-path

```

# Windows (PowerShell)
```
if (-not (Test-Path "$env:USERPROFILE\.ralen")) { New-Item -ItemType Directory -Path "$env:USERPROFILE\.ralen" | Out-Null }; Invoke-WebRequest https://github.com/serene1491/Ralen/releases/latest/download/ralenWin64.exe -OutFile "$env:USERPROFILE\.ralen\Ralen.exe"; & "$env:USERPROFILE\.ralen\Ralen.exe" configure-path
```


Languages:
SaLang>
    0.1.0
    0.1.1
