# Remote Clipboard

Portapapeles compartido, seguro y transparente entre computadores Windows de una red local.
Copias con **Ctrl+C** en un equipo y pegas con **Ctrl+V** en otro, también en Windows Server y
sesiones RDP.

> **Estado:** FASE 0 completada (arquitectura + estructura inicial). La sincronización real llega
> en la FASE 1. Todavía no hay una versión instalable.

## Características (objetivo del MVP)

- Texto plano Unicode: español, emojis, saltos de línea.
- Sincronización bidireccional automática entre equipos vinculados de la LAN.
- Vinculación explícita con código de 6 dígitos (J-PAKE, resistente a ataques offline).
- Cifrado TLS con autenticación mutua y pinning de clave pública.
- Prevención de bucles de sincronización (3 capas).
- Reconexión automática, ejecución en segundo plano, icono en la bandeja.
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

## Requisitos y Windows compatibles

| Sistema | Soporte |
|---|---|
| Windows 10 / Windows 11 (x64) | Objetivo principal |
| Windows Server 2016 / 2019 / 2022 / 2025 (con Experiencia de escritorio) | Objetivo (FASE 3) |
| Windows Server Core | No soportado (sin escritorio interactivo) |

El usuario final **no necesita instalar .NET** ni otras dependencias (publicación *self-contained*).

## Instalación y uso

*Disponible a partir de la FASE 4 (`RemoteClipboardSetup.exe`).* Flujo previsto:

1. Ejecutar el instalador en cada equipo.
2. En el equipo A: bandeja → **+ Vincular dispositivo** → se muestra un código, p. ej. `847291`.
3. En el equipo B: **+ Vincular dispositivo** → introducir la IP de A (o elegirlo de la lista) y el código.
4. Listo: Ctrl+C en uno, Ctrl+V en el otro, en ambos sentidos.

## Seguridad y networking (resumen)

- Puerto **TCP 47800** (47801–47809 en servidores multiusuario), sólo perfiles Privado/Dominio y subred local.
- Puerto **UDP 47810** para descubrimiento (FASE 2, opcional).
- Identidad por usuario/equipo: GUID + certificado ECDSA P-256 protegido con DPAPI.
- Detalles en [docs/SECURITY.md](docs/SECURITY.md) y [docs/NETWORKING.md](docs/NETWORKING.md).

## Estructura del proyecto

```
src/RemoteClipboard.Core      Dominio, protocolo, seguridad, vinculación, sincronización (multiplataforma)
src/RemoteClipboard.Windows   Adaptadores Win32: portapapeles, DPAPI, instancia única, autoarranque
src/RemoteClipboard.App       Aplicación WPF de bandeja (raíz de composición)
tests/RemoteClipboard.Core.Tests
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

Publicación (FASE 4):

```bash
dotnet publish src/RemoteClipboard.App -c Release -r win-x64 --self-contained -p:PublishReadyToRun=true
```

Reglas del repositorio: nada de secretos en Git (ver `.gitignore`), nunca registrar contenido del
portapapeles, eventos de log sólo en `Core/Logging/Log.cs`, commits convencionales (`feat:`, `fix:`, `docs:`, `chore:`).

## Roadmap

- [x] **FASE 0 — Arquitectura**: análisis, stack, estructura, primitivas base con pruebas.
- [ ] **FASE 1 — MVP**: monitor de portapapeles, identidad, TLS en LAN, vinculación, sync bidireccional, anti-loops.
- [ ] **FASE 2 — Aplicación completa**: bandeja, UI moderna, descubrimiento, lista de dispositivos, configuración.
- [ ] **FASE 3 — Windows Server**: RDP, multiusuario, separación de sesiones.
- [ ] **FASE 4 — Distribución**: instalador, autoarranque, firewall, actualización, firma.
- [ ] **FASE 5 — Avanzado**: imágenes, archivos, historial opcional, políticas (solo enviar / solo recibir).
- [ ] **FASE 6 — Remoto**: relay entre redes (no planificado aún).

## Limitaciones conocidas

- Aún no sincroniza (FASE 0).
- Sólo LAN; redes Wi-Fi con aislamiento de clientes impiden la comunicación directa.
- Sólo texto en el MVP.
- Un mismo usuario con dos sesiones simultáneas en un servidor: sólo una ejecuta el agente.
- Sin firma de código hasta la FASE 4 (SmartScreen puede advertir).
