# Seguridad — Remote Clipboard

Principio: **el contenido del portapapeles es información sensible**. No viaja en claro, no se guarda,
no se registra y sólo llega a dispositivos que el usuario autorizó explícitamente.

No se inventa criptografía: sólo TLS de la plataforma (SChannel/OpenSSL vía `SslStream`), ECDSA,
SHA-256, HMAC, HKDF, DPAPI y J-PAKE (RFC 8236, implementación de BouncyCastle).

## Modelo de amenazas (MVP, LAN)

| Atacante | Objetivo | Defensa |
|---|---|---|
| Equipo pasivo en la LAN | Leer el portapapeles | TLS en todas las conexiones. |
| Equipo activo en la LAN (MITM, ARP spoofing) | Suplantar a un peer | TLS mutuo + pin de clave pública; vinculación con PAKE ligada a los pins. |
| Equipo no vinculado | Enviar/recibir contenido | El handshake TLS falla si su certificado no está en la lista de peers. |
| Adivinar el código de vinculación | Vincularse sin permiso | J-PAKE: sólo 1 intento online por ejecución; código de un solo uso, 2 min, 3 fallos ⇒ código quemado. |
| Otro usuario del mismo Windows Server | Leer el portapapeles o robar la identidad | Agente por usuario en su sesión, identidad cifrada con DPAPI CurrentUser, lista de peers por usuario. |
| Lectura de logs/archivos | Recuperar contenido | Nunca se registra ni se guarda contenido; huellas HMAC sólo en memoria con clave aleatoria. |
| Peer malicioso vinculado | DoS por mensajes enormes | Límite de trama (1 MiB) y de contenido (32 MiB) verificado **antes** de reservar memoria. |
| Anuncios de descubrimiento falsificados | Redirigir o "envenenar" la dirección de un equipo vinculado | El anuncio es sólo un candidato de conexión; la dirección se guarda únicamente tras verificar el pin en TLS. Datagramas ≤ 1 KB, validados y saneados. |
| Peer vinculado que reenvía contenido ajeno | Inyectar contenido "de otro" | `OriginDeviceId` debe ser el del peer autenticado; nada se retransmite. |

Fuera de alcance del MVP: malware ejecutándose como el mismo usuario (puede leer el portapapeles
igualmente).

## Identidad del dispositivo

- `DeviceId` = GUID aleatorio generado en el primer arranque (no el nombre del equipo).
- Certificado autofirmado **ECDSA P-256**, validez 10 años, EKU serverAuth + clientAuth.
- Se guarda como PKCS#12 cifrado con **DPAPI (CurrentUser)** en `%LOCALAPPDATA%\RemoteClipboard\secrets`.
- **Clonado/reinstalación:** la identidad guarda `MachineGuid` + SID del usuario. Si no coinciden al
  cargar (disco o VM clonados, perfil copiado), se genera una identidad nueva y hay que volver a
  vincular. Si DPAPI no puede descifrar el blob (otro usuario/equipo), se trata como inexistente.
  Renombrar el equipo no cambia la identidad. Reinstalar conservando el perfil la conserva.

## Autenticación y cifrado del canal

- `SslStream` con certificado de cliente obligatorio (TLS mutuo). Versión negociada por el SO:
  TLS 1.3 donde exista (Windows 11 / Server 2022+), si no TLS 1.2.
- La confianza **no** usa CA ni nombre de host: se acepta el certificado del peer sólo si
  `SHA-256(SubjectPublicKeyInfo)` coincide con un pin guardado en la vinculación (`CertificatePin`,
  comparación en tiempo constante). El handshake TLS prueba la posesión de la clave privada.
- Revocación deshabilitada a propósito: no hay CA y evitaría cualquier tráfico a Internet.
- Implementado en `Core/Security/PinnedTls.cs` y probado con sockets reales en `PinnedTlsTests`.

## Vinculación (pairing)

Un código de 6 dígitos tiene ~20 bits de entropía. Usarlo directamente (hash o HMAC del código) sería
inseguro: un MITM capturaría el valor y probaría el millón de códigos *offline* en milisegundos. Por eso
el código se usa como contraseña de un **PAKE** (Password-Authenticated Key Exchange), que no revela
nada que permita ataques offline.

Protocolo (A = equipo que muestra el código, B = equipo que lo introduce):

1. A abre una ventana de vinculación: código aleatorio (`RandomNumberGenerator`), 2 minutos, máximo 3
   intentos fallidos, un solo uso (`PairingWindow`). A muestra el código y su IP:puerto.
2. B introduce IP (o elige un equipo descubierto) y el código.
3. B abre TLS con A. En modo vinculación cada lado acepta cualquier certificado pero registra su pin
   (`pinA`, `pinB`).
4. Dentro del canal TLS (mensajes `pairRequest` → `pairRound1/2` → `pairConfirm` → `pairResult`): rondas 1 y 2 de **J-PAKE** (grupo NIST 3072 bits, SHA-256), con
   `participantId` = DeviceIds y contraseña = código ⇒ material de clave compartido.
5. Confirmación de clave **ligada al canal**: `K = HKDF-SHA256(material, info="RemoteClipboard-Pairing-v1")`;
   cada lado envía `HMAC-SHA256(K, rol ‖ idA ‖ idB ‖ pinA ‖ pinB)` y verifica el del otro en tiempo
   constante. Un MITM tendría pins distintos en cada tramo ⇒ las MAC no coinciden; y sin el código no
   puede calcular `K` (sólo 1 intento por ejecución, 3 por código).
6. Éxito ⇒ ambos guardan `PairedDevice { DeviceId, nombre, pin }`. El código queda consumido.
   Cualquier desviación del protocolo (incluido abortar a mitad) cuenta como intento fallido.

Implementación: `Core/Pairing/PairingProtocol.cs`. Pruebas: código correcto, código incorrecto, MITM con
el código correcto pero certificados propios (detectado), bloqueo tras 3 intentos, invitación cerrada.

Alternativas evaluadas: clave simétrica compartida (sin revocación por dispositivo), CA propia
(compleja), tokens *bearer* (si se filtran, suplantación), comparación visual de código SAS (segura,
pero no encaja con "introduzca el código mostrado"; queda como plan B sin dependencias).

## Rotación y revocación

- **Desvincular** = borrar el pin ⇒ el peer queda rechazado en el siguiente handshake.
- **Rotación** (futuro): mensaje `RotateKey` dentro del canal autenticado con el nuevo pin. Mientras tanto,
  re-vincular rota las credenciales.

## Secretos

- Ningún secreto en código, Git, README o configuración. `.gitignore` excluye `*.pfx`, `*.pem`, `*.key`,
  `identity.*`, `peers.*`, `.env`, etc.
- Los pins de los peers **no** son secretos (son públicos), pero sí información de integridad: se guardan
  en el perfil del usuario.
- Credential Manager descartado: límite de tamaño del blob y usa DPAPI internamente.

## Privacidad

- Sin historial, sin nube, sin telemetría, sin estadísticas, sin servicios externos.
- `ClipboardContent.ToString()` y `ClipboardUpdateMessage.ToString()` no muestran el contenido.
- Los eventos de log están centralizados en `Core/Logging/Log.cs` y **ninguno** acepta contenido,
  longitud, código de vinculación o material de clave.
- Los búferes de tramas se ponen a cero tras usarlos.
- Se respetan los formatos de exclusión que usan los gestores de contraseñas
  (`ExcludeClipboardContentFromMonitorProcessing`, `CanUploadToCloud=0`, etc.): ese contenido ni se lee.
- Lo recibido de otro equipo se marca para que no vaya al portapapeles en la nube ni al historial de Windows.
- Una prueba de extremo a extremo verifica que ni el contenido copiado ni el código de vinculación
  aparecen en ningún log.
