# Arquitectura — Remote Clipboard (FASE 0)

> Documento de decisiones técnicas. Seguridad y redes tienen su propio documento:
> [SECURITY.md](SECURITY.md) y [NETWORKING.md](NETWORKING.md).

## 1. Resumen de la arquitectura

**Un agente por usuario, ejecutándose dentro de la sesión interactiva del usuario, como ese usuario,
sin elevación. Comunicación directa punto a punto (LAN) por TCP + TLS mutuo con pinning de clave
pública obtenido durante la vinculación.**

```
 ┌──────────────── PC A (sesión de Marlon) ───────────────┐        ┌──────────── PC B ────────────┐
 │  RemoteClipboard.exe (agente, bandeja del sistema)      │        │  RemoteClipboard.exe         │
 │                                                         │        │                              │
 │  Win32 Clipboard ──WM_CLIPBOARDUPDATE──► ClipboardMonitor│        │                              │
 │                                             │           │        │                              │
 │                                   ClipboardEchoGuard    │  TLS   │                              │
 │                                   (anti-loop)           │ mutuo  │                              │
 │                                             ▼           │ TCP    │                              │
 │                                        SyncEngine ──────┼────────┼──► SyncEngine ──► Writer ──► │
 │                                             ▲           │ 47800  │     (marca "origen remoto")  │
 │  Identity (GUID + cert ECDSA) ── DPAPI      │           │        │                              │
 │  PeerStore (pins de dispositivos vinculados)│           │        │                              │
 └─────────────────────────────────────────────────────────┘        └──────────────────────────────┘
```

Capas del código (arquitectura modular por capas, sin sobreingeniería):

| Proyecto | TFM | Responsabilidad |
|---|---|---|
| `RemoteClipboard.Core` | `net10.0` | Dominio, protocolo, seguridad (pins, TLS, J-PAKE), vinculación, sincronización, anti-loops, eventos de log. **Sin APIs de Windows** → se prueba en cualquier SO. |
| `RemoteClipboard.Windows` | `net10.0-windows` | Adaptadores Win32: portapapeles, DPAPI, rutas, instancia única, autoarranque, firewall. Implementa interfaces de Core. |
| `RemoteClipboard.App` | `net10.0-windows` (WPF) | Raíz de composición, bandeja del sistema, UI. |
| `RemoteClipboard.Core.Tests` | `net10.0` | Unitarias + integración (TLS real sobre loopback). |

Dependencias: `App → Windows → Core`. Core no conoce a Windows ni a la UI.

## 2. Stack tecnológico definitivo

| Elemento | Decisión | Motivo / alternativa descartada |
|---|---|---|
| Lenguaje / runtime | **C# 14 + .NET 10 (LTS, soporte hasta nov-2028)** | .NET 8 sale de soporte en nov-2026. Self-contained: el usuario no instala nada. |
| UI | **WPF** + `NotifyIcon` de WinForms para la bandeja | WPF permite una UI moderna con estilos propios y funciona en Windows Server con Experiencia de escritorio. **WinUI 3** descartado: depende del Windows App SDK, peor soporte en Server y más peso. **WinForms puro** es más ligero pero difícil de hacer "moderno". |
| Detección del portapapeles | `AddClipboardFormatListener` + `WM_CLIPBOARDUPDATE` en ventana *message-only* | Basado en eventos (sin polling), disponible desde Vista. |
| Transporte | **TCP + TLS (SslStream), autenticación mutua** | UDP: sin fiabilidad/orden, tamaño limitado. WebSocket: capa HTTP innecesaria en LAN (se puede añadir para el relay de la Fase 6 tras la misma abstracción). Named Pipes: sólo IPC local (útil en el futuro servicio ↔ agente). |
| Serialización | `System.Text.Json` con *source generator*, tramas con prefijo de longitud | Sin reflexión, rápido, extensible (tipos polimórficos con discriminador). |
| Identidad | GUID + certificado autofirmado **ECDSA P-256** por usuario/equipo | Ver SECURITY.md. |
| Vinculación | **J-PAKE** (BouncyCastle) con código de 6 dígitos + confirmación de clave ligada a los pins TLS | Un código corto no puede usarse como clave sin PAKE (ataque offline). |
| Secretos en reposo | **DPAPI (CurrentUser)** | Credential Manager limita el tamaño del blob y usa DPAPI internamente. |
| Descubrimiento (Fase 2) | **UDP multicast/broadcast propio** + **IP manual** | mDNS nativo (`DnsServiceRegister`) no existe en Windows Server 2016; implementar mDNS completo es innecesario. El descubrimiento sólo es una *pista*: la confianza la da el pin TLS. |
| Logging | `Microsoft.Extensions.Logging` + `LoggerMessage` (source-generated) + proveedor de archivo propio mínimo (Fase 1) | Evita dependencia de Serilog; los eventos están centralizados en `Core/Logging/Log.cs`, sin parámetros de contenido. |
| Tests | xUnit v3 sobre Microsoft.Testing.Platform | |
| Instalador | **Inno Setup** + publicación self-contained | Ver §12. |

## 3–5. Networking, seguridad y vinculación

Ver [NETWORKING.md](NETWORKING.md) y [SECURITY.md](SECURITY.md).

## 6. Estrategia de portapapeles

**Detección (sin polling):**
1. Hilo dedicado con bucle de mensajes Win32 y una ventana *message-only* (`HWND_MESSAGE`).
2. `AddClipboardFormatListener(hwnd)` → Windows envía `WM_CLIPBOARDUPDATE` (0x031D) en cada cambio.
3. `GetClipboardSequenceNumber()` para descartar notificaciones duplicadas.

**Lectura:**
1. `OpenClipboard(hwnd)` con reintentos acotados (p. ej. 5 intentos, 20→160 ms) porque otra
   aplicación puede tenerlo abierto. No es polling: sólo ocurre tras un evento.
2. Si existe uno de los formatos de privacidad documentados por Microsoft, **no se sincroniza**:
   `ExcludeClipboardContentFromMonitorProcessing`, `CanUploadToCloud = 0`,
   `CanIncludeInClipboardHistory = 0`, y el formato heredado `Clipboard Viewer Ignore`
   (usados por gestores de contraseñas). → `ClipboardChange.IsExcludedByOwner`.
3. Si está presente nuestro formato privado `RemoteClipboard.RemoteOrigin` → cambio de origen remoto.
4. `IsClipboardFormatAvailable(CF_UNICODETEXT)` → `GetClipboardData` → `GlobalLock`, lectura
   acotada por `GlobalSize` y por el límite del protocolo (4 MiB).

**Escritura (contenido remoto):** `OpenClipboard` → `EmptyClipboard` → `SetClipboardData(CF_UNICODETEXT)`
+ `SetClipboardData(RemoteClipboard.RemoteOrigin)` → `CloseClipboard`. Ctrl+V funciona en cualquier
aplicación porque es un CF_UNICODETEXT normal.

**Formatos futuros:** `ClipboardFormat` (valores estables del protocolo) + `ClipboardContent` (bytes +
formato). Imágenes (CF_DIBV5/PNG), RTF, HTML y archivos se añaden como nuevos valores y lectores, sin
cambiar transporte ni sincronización.

## 7. Estrategia para Windows Server

- **Un Windows Service no puede leer el portapapeles de los usuarios.** Desde Vista, los servicios corren
  en la sesión 0 aislada; el portapapeles pertenece a la *window station* (`WinSta0`) de cada sesión.
  Usar un servicio exigiría lanzar procesos en sesiones de usuario con `CreateProcessAsUser` como SYSTEM:
  más complejo, más privilegios y más superficie de ataque, sin ganancia para el MVP.
- **Decisión: agente por usuario (no servicio)**, iniciado al iniciar sesión (clave `Run`), ejecutándose
  como el usuario en su propia sesión. Es el mismo modelo que `rdpclip.exe`.
- **Arquitectura híbrida futura (Fase 4+/empresa):** un servicio opcional *sin acceso al portapapeles*
  para políticas centralizadas, actualización o relay, comunicado con cada agente por Named Pipe con ACL
  por usuario. No se implementa ahora.
- **Windows Server Core** (sin escritorio) no es objetivo: no hay sesión interactiva con bandeja.

## 8. Estrategia para RDP y multiusuario

Garantía central: **el contenido de un usuario nunca puede acabar en el portapapeles de otro.**

| Mecanismo | Efecto |
|---|---|
| Un agente por usuario, en su sesión | Sólo puede leer/escribir el portapapeles de su propia sesión. |
| Identidad y claves por perfil (`%LOCALAPPDATA%`, DPAPI CurrentUser) | Otro usuario del servidor no puede descifrar ni reutilizar la identidad. |
| Lista de vinculación por usuario | El PC de Marlon se vincula con *el agente de Marlon en el servidor*, no con "el servidor". |
| Puerto por agente (47800, o el siguiente libre hasta 47809) | Dos usuarios del mismo servidor tienen listeners distintos. Una conexión al agente equivocado falla en TLS porque su pin no coincide. |
| Instancia única **por usuario** (lock exclusivo en su `%LOCALAPPDATA%`) | Si el mismo usuario abre dos sesiones RDP, sólo una ejecuta el agente (evita identidades duplicadas). Un `Global\` mutex se descartó: otro usuario podría ocuparlo (DoS). |

**Redirección de portapapeles de RDP (rdpclip):** si un usuario conecta por RDP de PC A al servidor con
la redirección activada *y* ambos agentes están vinculados, el mismo texto llega por dos caminos. El
`ClipboardEchoGuard` (huella del último contenido sincronizado) evita reenvíos y bucles; se documentará
la recomendación de usar uno de los dos mecanismos.

**A validar en Fase 3:** comportamiento en sesiones RDP desconectadas, bloqueo de pantalla, y con
"Restringir a cada usuario a una sola sesión" desactivado.

## 9. Prevención de loops

Tres capas independientes (cualquiera de ellas sola ya rompe el ciclo A→B→A):

1. **Marca de origen remoto** — al escribir contenido recibido se añade el formato privado
   `RemoteClipboard.RemoteOrigin`; el `WM_CLIPBOARDUPDATE` resultante se reconoce y no se reenvía.
2. **Huella del estado compartido** — `ClipboardEchoGuard` recuerda el último contenido sincronizado
   *en cualquier dirección* (HMAC-SHA256 con clave aleatoria en memoria; nunca el texto). Un cambio
   local idéntico no se reenvía (cubre apps que reescriben el portapapeles, rdpclip, o que otra app
   elimine nuestra marca). Copiar de nuevo "x" después de recibir "y" **sí** se envía (cambio real).
3. **Sin reenvío** — un dispositivo sólo emite cambios originados localmente; lo recibido nunca se
   retransmite. Además: `OriginDeviceId` debe coincidir con el peer autenticado y `MessageId` se
   deduplica (`MessageDeduplicator`) para reconexiones y el futuro relay.

Probado en `ClipboardEchoGuardTests` (incluida una simulación A↔B con y sin pérdida de la marca).

## 10. Estructura de carpetas

```
remote-clipboard/
├─ RemoteClipboard.sln
├─ Directory.Build.props / Directory.Packages.props / global.json
├─ src/
│  ├─ RemoteClipboard.Core/
│  │  ├─ Clipboard/   (ClipboardContent, ClipboardFormat, IClipboardMonitor, IClipboardWriter)
│  │  ├─ Devices/     (DeviceId, DeviceInfo, PairedDevice)
│  │  ├─ Logging/     (Log: eventos sin contenido)
│  │  ├─ Pairing/     (PairingCode, PairingWindow; J-PAKE en Fase 1)
│  │  ├─ Protocol/    (mensajes, FrameCodec, límites)
│  │  ├─ Security/    (CertificatePin, DeviceCertificateFactory, PinnedTls, ISecretStore)
│  │  └─ Sync/        (ClipboardEchoGuard, MessageDeduplicator, SyncDirection; SyncEngine en Fase 1)
│  ├─ RemoteClipboard.Windows/
│  │  ├─ Security/    (DpapiSecretStore)
│  │  └─ Storage/     (AppPaths, SingleInstanceLock)
│  └─ RemoteClipboard.App/  (WPF, Tray/)
├─ tests/RemoteClipboard.Core.Tests/
├─ installer/        (Inno Setup, Fase 4)
├─ docs/             (ARCHITECTURE, SECURITY, NETWORKING)
└─ .github/workflows/ci.yml
```

## 11. Dependencias

| Paquete | Uso | Justificación |
|---|---|---|
| `Microsoft.Extensions.Logging(.Abstractions)` | Logging | Estándar de .NET. |
| `System.Security.Cryptography.ProtectedData` | DPAPI | Paquete oficial de Microsoft. |
| `BouncyCastle.Cryptography` (Fase 1) | J-PAKE | .NET no incluye ningún PAKE. Librería criptográfica madura (MIT). Sólo se usa en la vinculación. |
| `xunit.v3` | Tests | — |

Nada más. Sin telemetría, sin servicios externos.

## 12. Estrategia de instalación

- **Publicación:** `dotnet publish -c Release -r win-x64 --self-contained` (ReadyToRun; sin *trimming*
  porque WPF no lo soporta). `win-arm64` más adelante.
- **Instalador: Inno Setup** → `RemoteClipboardSetup.exe`. Simple, scriptable en CI, sin runtime extra,
  maneja Program Files, accesos directos, autoarranque y reglas de firewall.
  - *WiX/MSI*: ideal para despliegue empresarial por GPO/Intune → se añadirá en la fase empresarial.
  - *MSIX*: requiere firma obligatoria, no existe en Server 2016 y complica reglas de firewall y el
    autoarranque multiusuario.
- **Instalación por equipo** (Program Files, requiere admin una vez) + autoarranque por usuario.
- **Firewall:** una regla de entrada creada por el instalador (ver NETWORKING.md).
- **Firma Authenticode** del instalador y del exe en Fase 4 (evita SmartScreen).

## 13. Estrategia de testing

| Nivel | Qué | Dónde |
|---|---|---|
| Unitarias (Core) | Texto/Unicode/emojis/saltos de línea/vacío, framing, anti-loops, códigos, ventanas de vinculación, pins | Linux y Windows (CI) |
| Integración (Core) | TLS mutuo real en loopback: autorizado, no autorizado, pin incorrecto. Fase 1: dos agentes en proceso con portapapeles falso (A→B, B→A, consecutivos, desconexión, reconexión, cambio de puerto/IP, J-PAKE con código incorrecto) | Linux y Windows |
| Integración Windows | Portapapeles Win32 real (lectura/escritura/marca/formatos de exclusión) | Windows, local (los runners de CI no siempre tienen escritorio interactivo) |
| Manual / E2E | Plan con 2 PCs o VMs, Windows Server con 2 usuarios RDP, reinicios, cable/Wi-Fi | Checklist en `docs/` (Fase 1–3) |

## 14. Roadmap técnico (FASE 1 en detalle)

| Hito | Commit previsto |
|---|---|
| 1.1 Monitor/lector/escritor Win32 del portapapeles + marca de origen + formatos de exclusión | `feat: implement clipboard monitoring` |
| 1.2 Identidad del dispositivo (GUID + cert, DPAPI, detección de clonado) y almacén de peers | `feat: add device identity` |
| 1.3 Listener/dialer TLS, gestor de conexiones, heartbeat, backoff | `feat: add LAN communication` |
| 1.4 Vinculación J-PAKE + confirmación ligada a pins | `feat: implement device pairing` |
| 1.5 `SyncEngine` bidireccional (con política por dirección preparada) | `feat: implement bidirectional clipboard sync` |
| 1.6 UI mínima: mostrar/introducir código, estado, logging a archivo | `feat: add pairing UI and file logging` |

Fases 2–5 según el roadmap del producto (README). Fase 6 (relay) **no** se implementa.

## 15. Riesgos técnicos

| Riesgo | Mitigación |
|---|---|
| `OpenClipboard` falla porque otra app lo tiene abierto | Reintentos acotados tras el evento; nunca polling. |
| Apps que generan muchos eventos (Excel, renderizado diferido) | Sólo se lee CF_UNICODETEXT; secuencia del portapapeles; límite 4 MiB. |
| Diferencias de SChannel: TLS 1.3 sólo en Win11/Server 2022+; claves efímeras no válidas para servidor TLS | TLS negociado por el SO (1.2 mínimo); claves persistidas al importar el PKCS#12; matriz de pruebas en SO reales. |
| Firewall/antivirus/GPO corporativa bloquea reglas locales | Regla mínima documentada; diagnóstico claro en la UI. |
| Wi-Fi con aislamiento de clientes impide P2P | Documentado; relay en Fase 6. |
| Conflicto de puertos multiusuario en Server | Rango 47800–47809, el puerto real se anuncia en `Hello` y en el descubrimiento. |
| VM/disco clonado ⇒ identidad duplicada | Identidad ligada a `MachineGuid` + SID; si no coinciden se regenera (re-vincular). Detección de `DeviceId` duplicado en la red. |
| Binario sin firmar ⇒ SmartScreen | Firma Authenticode en Fase 4. |
| Huella de memoria de WPF (~60–90 MB) | Ventana creada bajo demanda; el resto del tiempo sólo bandeja. |
| Dependencia BouncyCastle | Aislada en la vinculación; alternativa documentada (comparación de código SAS). |

## 16. Decisiones pendientes (con recomendación por defecto)

1. **Visibilidad del repositorio** — actualmente es **público**; se pidió privado. → Cambiarlo en *Settings → General → Danger Zone → Change visibility*.
2. **Autoarranque en Windows Server** — ¿para todos los usuarios (HKLM\Run) o opt-in por usuario (HKCU\Run)? → *Recomendado: opt-in por usuario en Server, para todos en escritorio.*
3. **Contenido remoto y la nube de Windows** — marcar lo recibido con `CanUploadToCloud=0` para que no suba al portapapeles en la nube/historial de Windows. → *Recomendado: sí (privacidad por defecto).*
4. **Tamaño máximo de texto** — → *4 MiB.*
5. **Idioma de la UI** — → *Español, con recursos preparados para inglés.*
6. **Certificado de firma de código** (Fase 4) — compra de certificado o Azure Trusted Signing.
