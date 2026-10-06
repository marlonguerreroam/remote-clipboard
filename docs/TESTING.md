# Pruebas — Remote Clipboard

## Automáticas (`dotnet test`)

| Proyecto | Dónde corre | Cubre |
|---|---|---|
| `RemoteClipboard.Core.Tests` | Linux y Windows (CI) | Texto/Unicode/emojis/saltos de línea/vacío, fragmentación de textos largos, framing, anti-loops, códigos y ventanas de vinculación, pins, TLS mutuo real, J-PAKE (código correcto/incorrecto/MITM), identidad (clonado), almacén de peers, logger de archivo, parser de direcciones y **pruebas de extremo a extremo** con agentes reales sobre TLS en loopback |
| `RemoteClipboard.Windows.Tests` | Sólo Windows | Portapapeles Win32 real: lectura exacta, escritura con marca de origen, evento sin polling, contenido privado no leído |

Descubrimiento (`EndToEnd/DiscoveryTests.cs`): detección mutua y del estado "mostrando código",
expiración, reconexión cuando **ambos** equipos cambian de dirección, anuncios falsificados (no
redirigen ni envenenan la dirección guardada), datagramas malformados y nombres saneados.

Escenarios de extremo a extremo (`EndToEnd/EndToEndTests.cs`): vinculación + A→B + B→A, texto largo
(~6.6 MB), cambios consecutivos, sin loops (con y sin marca), códigos incorrectos y bloqueo, invitación
cerrada, dispositivo no autorizado, reinicio en otro puerto (≈ cambio de IP), equipo apagado y vuelve,
heartbeat ante peer silencioso (≈ cable desconectado), sincronización desactivada, solo enviar / solo
recibir, desvinculación,
tres dispositivos, y ausencia de contenido y códigos en los logs.

## Manual (dos equipos reales)

Build portable: *Releases* → borrador privado `vX.Y.Z-binarios` (generado por el workflow *Release*) →
`RemoteClipboard-vX.Y.Z-win-x64.zip`, o compilación local con `dotnet publish` (ver README) →
descomprimir y ejecutar `RemoteClipboard.exe` en cada equipo.

Firewall (hasta que exista el instalador), en una consola de administrador de cada equipo:

```
netsh advfirewall firewall add rule name="Remote Clipboard (TCP)" dir=in action=allow program="RUTA\RemoteClipboard.exe" protocol=TCP localport=47800-47809 profile=private,domain remoteip=localsubnet
netsh advfirewall firewall add rule name="Remote Clipboard (descubrimiento)" dir=in action=allow program="RUTA\RemoteClipboard.exe" protocol=UDP localport=47810 profile=private,domain remoteip=localsubnet
```

| # | Prueba | Resultado esperado |
|---|---|---|
| 1 | A: Vincular → Mostrar código. B: Vincular → Introducir código con la IP de A | Ambos muestran al otro como 🟢 Conectado |
| 2 | Copiar texto en A, Ctrl+V en B (Bloc de notas, Word, navegador) | Texto idéntico, incluidos acentos, emojis y saltos de línea |
| 3 | Copiar en B, Ctrl+V en A | Idem |
| 4 | Copiar un texto de varios MB | Llega completo |
| 5 | Copiar desde un gestor de contraseñas (KeePass, 1Password, Bitwarden) | **No** se sincroniza |
| 6 | Código incorrecto 3 veces | "Código incorrecto" ×2, luego "Demasiados intentos" |
| 7 | Desconectar el cable/Wi-Fi de B 1 min y reconectar | B pasa a ⚪ y vuelve a 🟢 solo |
| 8 | Reiniciar B (o cerrar y abrir la app) | Se reconecta solo |
| 9 | Cambiar la IP de B (DHCP/renovar) | Se reconecta solo (B vuelve a conectar con A) |
| 10 | Sincronización OFF en B | Nada entra ni sale de B |
| 11 | Desvincular en A | B pasa a ⚪ y no vuelve a conectar |
| 12 | Revisar `%LOCALAPPDATA%\RemoteClipboard\logs` | Sólo eventos técnicos, nunca texto copiado |
| 13 | Windows Server con dos usuarios RDP, cada uno vinculado a su PC | Cada usuario recibe sólo lo suyo (FASE 3) |
| 14 | B: Vincular → Introducir código | A aparece en "Equipos en la red"; con A mostrando código, aparece "Mostrando código" |
| 15 | Cambiar la IP de **ambos** equipos y reiniciar la app | Se reconectan solos (descubrimiento) |
| 16 | Configuración → Tema Oscuro / Claro / Sistema | Cambia al instante, incluida la barra de título |
| 16b | Windows 11: mover la ventana sobre un fondo de colores; desactivar *Configuración de Windows → Personalización → Colores → Efectos de transparencia* | Con transparencia: el fondo se ve desenfocado a través de la ventana. Sin ella (o en RDP / Windows 10): degradado opaco, todo legible |
| 17 | Opciones de un dispositivo → "Solo recibir" | Este equipo recibe pero no envía a ese dispositivo |
| 18 | Configuración → cambiar el nombre → Guardar → Reiniciar | El otro equipo muestra el nombre nuevo |
| 20 | Ejecutar `RemoteClipboardSetup` (sin portable previo) | Pide permisos de administrador, instala, abre la app; existen las reglas "Remote Clipboard (TCP)" y "(descubrimiento)" en el Firewall de Windows |
| 21 | Con la versión portable abierta y "Iniciar con Windows" activo, instalar | La portable se cierra; tras reiniciar sesión arranca la versión instalada |
| 22 | Instalar una versión nueva encima | Se conserva la configuración y los dispositivos vinculados |
| 23 | Desinstalar (Configuración de Windows → Aplicaciones) | Se cierra la app, desaparecen archivos, accesos y reglas de firewall; reinstalar recupera las vinculaciones |
| 19 | Bandeja → Salir; volver a abrir la app. Repetir con Configuración → Guardar → "Reiniciar ahora" | La app se cierra del todo (no queda `RemoteClipboard.exe` en el Administrador de tareas) y vuelve a abrir |
