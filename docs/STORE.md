# Publicación en la Microsoft Store

Guía para publicar Remote Clipboard en la Microsoft Store como paquete **MSIX**. La Store firma el
paquete, así que los usuarios no ven avisos de "editor desconocido".

## Qué cambia en la versión de la Store

| Tema | Instalador clásico | Microsoft Store (MSIX) |
|---|---|---|
| Firewall | Reglas creadas por el instalador (solo subred local, privada/dominio) | Reglas declaradas en el paquete (`installer/msix/AppxManifest.xml`), solo perfiles privado/dominio |
| Inicio con Windows | Opción de la app (registro `Run`) | Tarea de inicio del paquete, activa tras el primer arranque; se gestiona en *Configuración → Aplicaciones → Inicio* |
| Datos (`%LOCALAPPDATA%\RemoteClipboard`) | Se conservan al desinstalar | Windows los guarda en la carpeta privada del paquete y los **borra al desinstalar** |
| Windows Server | Sí | No (Server no tiene Store): usar el instalador clásico |

No instales ambas versiones a la vez para el mismo usuario: serían dos dispositivos distintos con dos
identidades. Si pasas del instalador a la Store, desinstala el primero y vuelve a vincular los equipos.

## 1. Probar el paquete antes de enviarlo (sideload)

Cada build del CI y cada release dejan en un **borrador privado** de *Releases*
`RemoteClipboard-…-test.msix`, firmado con un certificado de prueba de un solo uso, y su
`RemoteClipboard-…-test.cer`.

1. Descarga ambos archivos. Desinstala la versión clásica si la tienes.
2. En PowerShell **como administrador**, confía en el certificado de prueba (solo para el paquete de
   prueba; cada build trae uno nuevo):
   ```powershell
   Import-Certificate -FilePath .\RemoteClipboard-test.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```
3. Doble clic en el `.msix` → **Instalar**, o `Add-AppxPackage .\RemoteClipboard-test.msix`.
4. Comprueba los casos 24–27 de [TESTING.md](TESTING.md).
5. Al terminar, puedes borrar el certificado: `certlm.msc` → *Personas de confianza* → *Certificados*
   → "Remote Clipboard Test".

## 2. Cuenta y reserva del nombre

1. Regístrate como desarrollador individual en <https://storedeveloper.microsoft.com> (gratuito para
   cuentas individuales; confirma las condiciones vigentes al registrarte).
2. Partner Center → **Apps y juegos → Nueva aplicación** → reserva el nombre. "Remote Clipboard" es
   genérico y puede estar ocupado: ten 2 o 3 alternativas.
3. En la app → **Administración de productos → Identidad del producto** copia:
   `Package/Identity/Name`, `Package/Identity/Publisher` y `Package/Properties/PublisherDisplayName`.

## 3. Generar el paquete para la Store

En GitHub → *Settings → Secrets and variables → Actions → **Variables*** crea (no son secretos):

| Variable | Valor |
|---|---|
| `MSIX_IDENTITY_NAME` | `Package/Identity/Name` |
| `MSIX_PUBLISHER` | `Package/Identity/Publisher` (empieza por `CN=`) |
| `MSIX_PUBLISHER_DISPLAY_NAME` | `Package/Properties/PublisherDisplayName` |
| `MSIX_DISPLAY_NAME` | El nombre reservado, exactamente igual |

Luego *Actions → Release → Run workflow* con la versión. El borrador privado `vX.Y.Z-binarios` incluirá
`RemoteClipboard-vX.Y.Z-store.msix`: ese es el que se sube a Partner Center (sin firmar; la Store lo
firma). La versión del paquete debe ser mayor en cada envío.

## 4. Envío en Partner Center

- **Paquetes**: sube `…-store.msix`. Familia de dispositivos: Escritorio.
- **Precios y disponibilidad**: precio y, si quieres, **prueba gratuita** (recomendado para medir interés).
- **Propiedades**: categoría *Productividad* (o *Utilidades y herramientas*).
  - URL de la política de privacidad: `https://github.com/marlonguerreroam/remote-clipboard/blob/main/docs/PRIVACY.md`
  - Sitio web: `https://github.com/marlonguerreroam/remote-clipboard`
  - Contacto de soporte: los *issues* del repositorio.
- **Clasificación por edades**: cuestionario IARC (sin contenido sensible; la app no comparte ubicación ni
  datos con terceros; comunicación solo entre los equipos del propio usuario).
- **Justificación de `runFullTrust`** (campo de capacidades restringidas):
  > Remote Clipboard es una aplicación de escritorio WPF (.NET) clásica empaquetada con MSIX. Necesita
  > confianza total para escuchar cambios del portapapeles con AddClipboardFormatListener, proteger su
  > identidad con DPAPI, aceptar conexiones TLS de los equipos vinculados por el usuario en la red local y
  > mostrar un icono en la bandeja del sistema. No envía datos a Internet.
- **Notas para la certificación** (opciones de envío):
  > La sincronización necesita dos equipos Windows en la misma red local con la app instalada. Pasos:
  > 1) en el equipo A: Vincular dispositivo → Mostrar código; 2) en el equipo B: Vincular dispositivo →
  > Introducir código, elegir A en la lista (o escribir su IP) e introducir el código; 3) copiar texto en
  > A y pegarlo en B. Con un solo equipo se puede revisar la interfaz, la configuración y la bandeja. La
  > app no requiere cuenta ni conexión a Internet.

## 5. Ficha de la Store (texto propuesto)

**Descripción breve:**
> Copia en un PC y pega en otro. Portapapeles compartido, cifrado y privado para tu red local.

**Descripción:**
> Remote Clipboard comparte el portapapeles de texto entre tus equipos Windows de forma automática:
> copias con Ctrl+C en uno y pegas con Ctrl+V en otro. Funciona también con Windows Server y escritorio
> remoto (RDP).
>
> Privado por diseño: sin nube, sin cuentas y sin telemetría. El texto viaja cifrado (TLS con
> autenticación mutua) directamente entre tus equipos de la red local, nunca se guarda en disco ni en
> los registros, y solo llega a los equipos que tú vinculas con un código de un solo uso.

**Características:**
- Sincronización automática y bidireccional del texto copiado (hasta 32 MiB).
- Cifrado TLS con autenticación mutua y vinculación segura con código de 6 dígitos.
- Descubrimiento automático de tus equipos en la red local.
- Por equipo: enviar y recibir, solo enviar o solo recibir.
- Respeta los gestores de contraseñas: su contenido no se sincroniza.
- Sin nube, sin cuentas, sin historial y sin telemetría.
- Diseño moderno con modo claro y oscuro.

**Palabras clave:** portapapeles, compartir portapapeles, copiar y pegar, red local, LAN, RDP,
productividad, sincronizar.

**Capturas:** al menos una de 1366×768 o mayor (ventana principal con dispositivos, vinculación,
configuración; claro y oscuro).

## Posibles motivos de rechazo y cómo evitarlos

- **Falta de transparencia sobre el portapapeles**: la descripción ya dice qué se lee, a dónde va y que
  no sale de la red local. No la recortes.
- **Política de privacidad** ausente o inaccesible: el enlace debe abrir sin iniciar sesión.
- **No se puede probar**: las notas de certificación explican cómo hacerlo con dos equipos.
- **Nombre** que no coincide con el reservado: `MSIX_DISPLAY_NAME` debe ser idéntico.
