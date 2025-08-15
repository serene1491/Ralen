Installing with PATH

# Linux
```
curl -L https://github.com/serene1491/Ralen/releases/latest/download/ralenLinux64 -o ~/.ralen/ralen && chmod +x ~/.ralen/ralen && ~/.ralen/ralen configure-path
```

# Windows (PowerShell)
```
Invoke-WebRequest https://github.com/serene1491/Ralen/releases/latest/download/ralenWin64.exe -OutFile "$env:USERPROFILE\.ralen\ralen.exe"; & "$env:USERPROFILE\.ralen\ralen.exe" configure-path
```
