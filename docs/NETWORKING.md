# Networking — Remote Clipboard

## Puertos y firewall

| Puerto | Protocolo | Dirección | Fase | Motivo |
|---|---|---|---|---|
| **47800** (47801–47809 si está ocupado) | TCP | Entrada + salida | 1 | Canal TLS de sincronización y vinculación. El rango sólo se usa en Windows Server con varios usuarios (un agente por usuario). |
| **47810** | UDP | Entrada + salida | 2 | Descubrimiento en la LAN (anuncios broadcast). Opcional: se puede desactivar y usar IP manual. |

Regla de firewall creada por el instalador (una por puerto/protocolo):

```
netsh advfirewall firewall add rule name="Remote Clipboard (TCP)" dir=in action=allow ^
  program="%ProgramFiles%\Remote Clipboard\RemoteClipboard.exe" protocol=TCP localport=47800-47809 ^
  profile=private,domain remoteip=localsubnet
```

```
netsh advfirewall firewall add rule name="Remote Clipboard (descubrimiento)" dir=in action=allow ^
  program="%ProgramFiles%\Remote Clipboard\RemoteClipboard.exe" protocol=UDP localport=47810 ^
  profile=private,domain remoteip=localsubnet
```

- Sólo perfiles **Privado** y **Dominio** (nunca Público).
- Sólo **subred local**.
- Limitada al ejecutable.
- No se requiere ninguna regla de salida (Windows permite la salida por defecto).

## Topología

Malla directa entre dispositivos vinculados (sin servidor central en el MVP). Cada par mantiene **una**
conexión TCP persistente.

- Ambos lados escuchan y ambos pueden marcar. Si se crean dos conexiones simultáneas, se conserva la
  iniciada por el `DeviceId` menor (regla determinista en los dos extremos).
- Primer mensaje en cada sentido: `Hello` (versión de protocolo, DeviceId, nombre, SO, puerto de escucha).
  El `DeviceId` anunciado debe corresponder al pin autenticado en TLS.

## Protocolo

`[uint32 big-endian longitud][JSON UTF-8]` dentro de TLS. Mensajes: `hello`, `clipboard`, `ping`, `pong`,
`pairRequest`, `pairRound1`, `pairRound2`, `pairConfirm`, `pairResult`. Discriminador `type` estable; los
tipos nuevos (imágenes, archivos, rotación de claves) se añaden sin romper versiones anteriores.

**Textos largos:** cada copia (≤ 32 MiB) se envía como mensajes `clipboard` consecutivos de ≤ 256 KiB
(trama ≤ 1 MiB). El receptor valida cada cabecera (tamaño total, índice, origen) **antes** de reservar
memoria; una copia nueva abandona una transferencia incompleta (gana la más reciente).

**Conexiones duplicadas:** si ambos equipos se conectan a la vez, ambos conservan la conexión abierta
por el `DeviceId` menor. La otra se "retira": deja de usarse para enviar pero sigue leyendo 5 s, de modo
que ningún mensaje en vuelo se pierde.

## Fallos de red y reconexión

| Situación | Comportamiento |
|---|---|
| Wi-Fi/cable desconectado, equipo apagado o reiniciado | Heartbeat `ping` cada 15 s; sin respuesta en 45 s ⇒ desconectado. |
| Reintento | Backoff exponencial 1 s → 30 s con *jitter*. |
| Red recupera conectividad | `NetworkChange.NetworkAddressChanged` dispara un reintento inmediato. |
| Cambio de IP del peer | Se actualiza con las conexiones entrantes del peer (+ su puerto en `Hello`) y con los anuncios de descubrimiento (verificados por TLS antes de guardarse). Funciona aunque ambos equipos cambien de dirección a la vez. |
| Aplicación reiniciada | La identidad y los pins persisten; se reconecta sola. |
| Firewall bloqueando temporalmente | Se trata como desconexión; se reintenta con backoff. |

## Descubrimiento en la LAN

- **UDP 47810**, broadcast de cada subred IPv4 + `255.255.255.255`. Puerto compartido (`SO_REUSEADDR`),
  así cada agente de usuario de un Windows Server multiusuario recibe los anuncios.
- Mensajes JSON (≤ 1 KB): `announce` {id, nombre, SO, puerto TCP, "mostrando código"} cada ~15 s, al
  arrancar, al abrir/cerrar la vinculación y al cambiar la red; `query` pide a todos anunciarse ya
  (respuesta limitada a 1/s para no amplificar tormentas de broadcast).
- Los equipos que dejan de anunciarse desaparecen de la lista a los ~50 s.
- **No es fuente de confianza.** Para un dispositivo ya vinculado, la dirección anunciada es sólo un
  *candidato extra* de conexión; se guarda únicamente después de que el handshake TLS demuestre su
  identidad (pin). Un anuncio falso puede, como mucho, provocar un intento de conexión fallido.
- Se puede desactivar ("Visible en la red local" en Configuración); entonces se usa la IP manual.
- mDNS descartado: la API nativa no existe en Windows Server 2016 y no aporta nada aquí.

Gracias al descubrimiento, dos equipos vinculados se reencuentran aunque **ambos** cambien de IP/puerto.

## Futuro: relay (Fase 6, no implementado)

El transporte está detrás del framing; un relay WebSocket/TLS podría transportar las mismas tramas
cifradas extremo a extremo entre agentes, sin cambiar la sincronización ni la vinculación.
