# Raccourcis (GNU make : `winget install ezwinports.make`). Tout repose sur les scripts .ps1,
# on peut donc aussi les lancer directement sans make.
PS = powershell -NoProfile -ExecutionPolicy Bypass -File

.PHONY: build run release version tag clean help

help:
	@echo make build            compile Claudiometer.exe
	@echo make run              compile puis lance
	@echo make release          compile + dist/Claudiometer-VERSION.zip (+ .sha256)
	@echo make version V=1.2.0  change le numero de version dans Claudiometer.cs
	@echo make version          affiche la version courante
	@echo make tag              cree le tag git vVERSION (le push declenche la release GitHub)
	@echo make clean            supprime l'exe et dist/

build:
	$(PS) build.ps1

run: build
	powershell -NoProfile -Command "Stop-Process -Name Claudiometer -ErrorAction SilentlyContinue; Start-Process .\Claudiometer.exe"

release:
	$(PS) release.ps1

version:
	$(PS) version.ps1 $(V)

tag:
	powershell -NoProfile -Command "$$v = & .\version.ps1; git tag \"v$$v\"; Write-Host \"Tag v$$v cree. Pour publier : git push origin v$$v\""

clean:
	powershell -NoProfile -Command "Remove-Item -Recurse -Force Claudiometer.exe, dist -ErrorAction SilentlyContinue"
