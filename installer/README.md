# Instalador (Inno Setup)

`RemoteClipboard.iss` genera `RemoteClipboardSetup-vX.Y.Z.exe`. Lo construye el workflow *Release*
(y el CI en cada PR, para validar el script). Construcción local en Windows:

```powershell
dotnet publish src\RemoteClipboard.App -c Release -r win-x64 --self-contained -p:PublishReadyToRun=true -o publish\RemoteClipboard
.github\scripts\build-installer.ps1 -Version 0.3.0 -SourceDir publish\RemoteClipboard
```

## Qué hace

| Acción | Detalle |
|---|---|
| Archivos | `C:\Program Files\Remote Clipboard` (por equipo: sirve para todos los usuarios, también en Windows Server) |
| Accesos directos | Menú Inicio; escritorio opcional |
| Firewall | Dos reglas de entrada: TCP 47800-47809 y UDP 47810, sólo para `RemoteClipboard.exe`, sólo **subred local** y perfiles **Privado/Dominio** (nunca Público) |
| Actualización | Instalar la versión nueva encima: detiene la app, reemplaza los archivos y la vuelve a abrir. Datos y vinculaciones se conservan |
| Inicio con Windows | Lo gestiona la app (por usuario): activado por defecto en escritorio, opcional en Windows Server |
| Desinstalación | Detiene la app, elimina archivos, accesos y reglas de firewall, y el inicio con Windows del usuario que desinstala. **Conserva** `%LOCALAPPDATA%\RemoteClipboard` (identidad y vinculaciones) para una reinstalación; bórrala a mano para eliminarlo todo |

Requiere permisos de administrador (Archivos de programa y firewall). Windows 10 1607+ / Server 2016+, x64.

## Firma de código

El pipeline firma el `.exe` y el instalador **sólo si** existen los secretos del repositorio
`CODESIGN_PFX_BASE64` (certificado .pfx en base64) y `CODESIGN_PFX_PASSWORD`
(*Settings → Secrets and variables → Actions*). Sin certificado, Windows SmartScreen mostrará
"Editor desconocido" (Más información → Ejecutar de todas formas). Opciones: certificado OV/EV de una CA,
o Azure Trusted Signing.
