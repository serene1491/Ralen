Ralen is an easy-to-use and easy-to-install environment. To test it for yourself, install the equivalent binary and run it. Or, simply use the following commands to automatically install and configure the path.

# Linux
```
mkdir -p ~/.ralen
curl -L -o ~/.ralen/Ralen https://github.com/serene1491/Ralen/releases/latest/download/Ralen
chmod +x ~/.ralen/Ralen
~/.ralen/Ralen configure-path
~/.ralen/Ralen --version
```

# Windows (PowerShell)
```
if (-not (Test-Path "$env:USERPROFILE\.ralen")) { New-Item -ItemType Directory -Path "$env:USERPROFILE\.ralen" | Out-Null }
Invoke-WebRequest https://github.com/serene1491/Ralen/releases/latest/download/Ralen.exe -OutFile "$env:USERPROFILE\.ralen\Ralen.exe"
& "$env:USERPROFILE\.ralen\Ralen.exe" configure-path
& "$env:USERPROFILE\.ralen\Ralen.exe" --version
```


Languages:
SaLang>
    0.1.0
    0.1.1
