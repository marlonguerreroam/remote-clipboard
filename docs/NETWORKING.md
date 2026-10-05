# Networking — Remote Clipboard

## Puertos y firewall

| Puerto | Protocolo | Dirección | Fase | Motivo |
|---|---|---|---|---|
| **47800** (47801–47809 si está ocupado) | TCP | Entrada + salida | 1 | Canal TLS de sincronización y vinculación. El rango sólo se usa en Windows Server con varios usuarios (un agente por usuario). |
| **47810** | UDP | Entrada + salida | 2 | Descubrimiento en la LAN (anuncios multicast/broadcast). Opcional: se puede desactivar y usar IP manual. |

Regla de firewall creada por el instalador (una por puerto/protocolo):

```
netsh advfirewall firewall add rule name="Remote Clipboard (TCP)" dir=in action=allow ^
  program="%ProgramFiles%\Remote Clipboard\RemoteClipboard.exe" protocol=TCP localport=47800-47809 ^
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
| Cambio de IP del peer | Se actualiza con: conexiones entrantes del peer (+ su puerto en `Hello`) y anuncios de descubrimiento (Fase 2). Basta con que uno de los dos conserve su dirección; si ambos cambian a la vez, en el MVP hay que volver a vincular (el descubrimiento de la Fase 2 lo resuelve). |
| Aplicación reiniciada | La identidad y los pins persisten; se reconecta sola. |
| Firewall bloqueando temporalmente | Se trata como desconexión; se reintenta con backoff. |

## Descubrimiento (Fase 2)

UDP multicast (grupo de ámbito local) + broadcast de subred como respaldo, puerto 47810. El anuncio
contiene `DeviceId`, nombre, SO y puerto TCP. **No es fuente de confianza**: sólo una pista de dirección;
la autenticación la da siempre el pin TLS. mDNS descartado: la API nativa no existe en Windows Server
2016 y una implementación completa no aporta nada en este caso.

## Futuro: relay (Fase 6, no implementado)

El transporte está detrás del framing; un relay WebSocket/TLS podría transportar las mismas tramas
cifradas extremo a extremo entre agentes, sin cambiar la sincronización ni la vinculación.
