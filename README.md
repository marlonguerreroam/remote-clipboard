<img src="docs/images/icon.png" width="72" alt="Remote Clipboard" />

# Remote Clipboard

Portapapeles compartido, seguro y transparente entre computadores Windows de una red local.
Copias con **Ctrl+C** en un equipo y pegas con **Ctrl+V** en otro, también en Windows Server y
sesiones RDP.

> **Estado:** FASE 1 (MVP) completada: sincronización bidireccional cifrada de texto entre equipos
> vinculados de la LAN. Hay un build portable para probar; el instalador llega en la FASE 4.

## Características

- Texto plano Unicode: español, emojis, saltos de línea; textos largos (hasta 32 MiB por copia).
- Sincronización bidireccional automática entre equipos vinculados de la LAN.
- Vinculación explícita con código de 6 dígitos (J-PAKE, resistente a ataques offline).
- Cifrado TLS con autenticación mutua y pinning de clave pública.
- Prevención de bucles de sincronización (3 capas).
- Reconexión automática, ejecución en segundo plano, icono en la bandeja.
- Descubrimiento automático en la red local: al vincular eliges el equipo de una lista.
- Por dispositivo: enviar y recibir, solo enviar o solo recibir.
- Modo claro/oscuro que sigue a Windows.
- Privacidad: sin historial, sin nube, sin telemetría, **el contenido nunca se registra en logs**.

## Arquitectura

Un **agente por usuario** que se ejecuta en la sesión interactiva del usuario (también dentro de RDP),
sin privilegios de administrador. Detecta cambios con `AddClipboardFormatListener` (sin polling) y los
envía por **TCP + TLS mutuo** directamente a los dispositivos vinculados.

| Documento | Contenido |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Stack, capas, portapapeles, Windows Server/RDP, anti-loops, instalación, testing, riesgos, decisiones pendientes |
| [docs/SECURITY.md](docs/SECURITY.md) | Modelo de amenazas, identidad, TLS con pins, protocolo de vinculación, secretos, privacidad |
| [docs/NETWORKING.md](docs/NETWORKING.md) | Puertos, firewall, topología, protocolo, reconexión, descubrimiento |
| [docs/TESTING.md](docs/TESTING.md) | Pruebas automáticas y plan de pruebas manuales con dos equipos |

## Requisitos y Windows compatibles

| Sistema | Soporte |
|---|---|
| Windows 10 / Windows 11 (x64) | Objetivo principal |
| Windows Server 2016 / 2019 / 2022 / 2025 (con Experiencia de escritorio) | Objetivo (FASE 3) |
| Windows Server Core | No soportado (sin escritorio interactivo) |

El usuario final **no necesita instalar .NET** ni otras dependencias (publicación *self-contained*).

## Instalación y uso

**Ahora (build portable):** en GitHub → *Releases* → última versión → descargar
`RemoteClipboard-vX.Y.Z-win-x64.zip`. Descomprimir en cada equipo y ejecutar `RemoteClipboard.exe` (no
requiere instalar .NET). Permitir la app en el firewall para redes privadas (ver [docs/TESTING.md](docs/TESTING.md)).
Las compilaciones de cada commit también quedan 14 días como artefacto de *Actions → CI*.
Configuración (icono ⚙ de la ventana o menú de la bandeja): nombre visible, visibilidad en la red,
puerto, tema e inicio con Windows.
A partir de la FASE 4: `RemoteClipboardSetup.exe`.

### Vinculación de dispositivos

1. En el equipo A: icono de la bandeja → **Vincular dispositivo…** → **Mostrar código** (p. ej. `847 291`,
   válido 2 minutos). La ventana también muestra la IP de A.
2. En el equipo B: **Vincular dispositivo…** → **Introducir código** → elegir A en **Equipos en la red**
   (o escribir su IP) + código → **Vincular**.
3. Listo: Ctrl+C en uno, Ctrl+V en el otro, en ambos sentidos. Se reconectan solos tras reinicios,
   cortes de red o cambios de IP.

Desde la bandeja: abrir la ventana, ver el estado, activar/desactivar la sincronización, iniciar con
Windows (activado por defecto en escritorio; opcional en Windows Server) y salir.

Datos locales (por usuario): `%LOCALAPPDATA%\RemoteClipboard` — `secrets\` (identidad cifrada con DPAPI),
`peers.json` (dispositivos vinculados), `settings.json`, `logs\` (7 días).

## Seguridad y networking (resumen)

- Puerto **TCP 47800** (47801–47809 en servidores multiusuario), sólo perfiles Privado/Dominio y subred local.
- Puerto **UDP 47810** para descubrimiento en la LAN (desactivable en Configuración).
- Identidad por usuario/equipo: GUID + certificado ECDSA P-256 protegido con DPAPI.
- Vinculación con J-PAKE + confirmación ligada a los certificados (resiste MITM y ataques offline).
- Lo recibido no va al portapapeles en la nube ni al historial de Windows; el contenido de gestores de
  contraseñas no se sincroniza.
- Detalles en [docs/SECURITY.md](docs/SECURITY.md) y [docs/NETWORKING.md](docs/NETWORKING.md).

## Estructura del proyecto

```
src/RemoteClipboard.Core      Dominio, protocolo, seguridad, vinculación, sincronización (multiplataforma)
src/RemoteClipboard.Windows   Adaptadores Win32: portapapeles, DPAPI, instancia única, autoarranque
src/RemoteClipboard.App       Aplicación WPF de bandeja (raíz de composición)
tests/RemoteClipboard.Core.Tests     Unitarias + extremo a extremo (cualquier SO)
tests/RemoteClipboard.Windows.Tests  Portapapeles Win32 real (sólo Windows)
docs/                         Arquitectura, seguridad, networking
installer/                    Inno Setup (FASE 4)
```

## Desarrollo y compilación

Requisitos: **.NET SDK 10** (cualquier SO para compilar y probar Core; Windows para ejecutar la app).

```bash
dotnet build              # compila toda la solución (los proyectos Windows también compilan en Linux/macOS)
dotnet test               # ejecuta las pruebas
dotnet run --project src/RemoteClipboard.App   # sólo en Windows
```

Publicar una versión: *Actions → Release → Run workflow* con la versión (p. ej. `v0.2.0`), o subir un tag `vX.Y.Z`. El workflow *Release* compila, prueba, crea el tag y adjunta el
ZIP portable a una GitHub Release.

Publicación manual:

```bash
dotnet publish src/RemoteClipboard.App -c Release -r win-x64 --self-contained -p:PublishReadyToRun=true
```

Reglas del repositorio: nada de secretos en Git (ver `.gitignore`), nunca registrar contenido del
portapapeles, eventos de log sólo en `Core/Logging/Log.cs`, commits convencionales (`feat:`, `fix:`, `docs:`, `chore:`).

## Roadmap

- [x] **FASE 0 — Arquitectura**: análisis, stack, estructura, primitivas base con pruebas.
- [x] **FASE 1 — MVP**: monitor de portapapeles, identidad, TLS en LAN, vinculación, sync bidireccional, anti-loops.
- [x] **FASE 2 — Aplicación completa**: descubrimiento automático, configuración, dirección por dispositivo, modo oscuro, icono definitivo.
- [ ] **FASE 3 — Windows Server**: RDP, multiusuario, separación de sesiones.
- [ ] **FASE 4 — Distribución**: instalador, autoarranque, firewall, actualización, firma.
- [ ] **FASE 5 — Avanzado**: imágenes, archivos, historial opcional, políticas (solo enviar / solo recibir).
- [ ] **FASE 6 — Remoto**: relay entre redes (no planificado aún).

## Limitaciones conocidas

- Sólo LAN; redes Wi-Fi con aislamiento de clientes impiden la comunicación directa (y el descubrimiento).
- El menú de la bandeja no sigue el modo oscuro (control de WinForms).
- Sólo texto (imágenes/archivos en FASE 5). Máximo 32 MiB por copia.
- Sin instalador ni regla de firewall automática hasta la FASE 4.
- Un mismo usuario con dos sesiones simultáneas en un servidor: sólo una ejecuta el agente.
- Windows Server/RDP multiusuario diseñado pero aún no validado en servidores reales (FASE 3).
- Sin firma de código hasta la FASE 4 (SmartScreen puede advertir).
