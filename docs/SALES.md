# Venta en Gumroad

Guía para vender Remote Clipboard en [Gumroad](https://gumroad.com) con las licencias sin conexión de
la app ([LICENSING.md](LICENSING.md)). Gumroad aloja la página del producto, cobra y gestiona impuestos;
tú envías la clave de licencia por correo después de cada venta.

> Revisa las condiciones vigentes de Gumroad (comisiones, pagos a Colombia, mínimo de pago) al
> registrarte, y haz que un abogado revise los términos de esta guía antes de vender.

## 1. Configurar Gumroad (una sola vez)

1. Crea la cuenta en <https://gumroad.com> y completa **Settings → Payments**: cómo te pagan (banco o,
   si tu banco no está soportado, PayPal en USD).
2. **New product → Digital product**. Nombre: `Remote Clipboard`.
3. **Precio**: ver la sección 2. Crea dos versiones del producto (*Versions*): **Personal** y **Empresa**.
4. **Contenido** (lo que recibe el comprador): sube `RemoteClipboardSetup-vX.Y.Z.exe` del borrador
   privado `vX.Y.Z-binarios` y pega en el texto del contenido la sección "Después de comprar" (sección 4).
5. **Descripción**: pega el texto de la sección 3.
6. Activa **Ask for name** en el checkout (la licencia va a nombre del comprador).
7. Publica el producto y pásame su URL: la pongo en el botón **Comprar licencia** de la app
   (`LicensingConfig.PurchaseUrl`).

## 2. Precio (sugerencia)

Pago único, sin suscripción: encaja con una licencia que funciona sin conexión y sin servidor.

| Edición | Para quién | Sugerencia |
|---|---|---|
| Personal | Una persona, en sus propios equipos | 12 USD |
| Empresa | Por cada usuario de una empresa (incluye Windows Server / RDP) | 25 USD por usuario |

Son puntos de partida: tú decides. Con la prueba gratuita de 14 días el comprador ya sabe que le sirve.

## 3. Texto de la página del producto

### Español

**Remote Clipboard — Copia en un PC, pega en otro.**

Comparte el portapapeles entre tus equipos Windows de la red local: copias texto, imágenes o archivos
con Ctrl+C en uno y los pegas con Ctrl+V en otro. Funciona también con Windows Server y escritorio
remoto (RDP).

- **Texto** (hasta 32 MB), **imágenes** (capturas, imágenes copiadas) y **archivos y carpetas** (hasta
  128 MB por copia).
- **Privado por diseño**: sin nube, sin cuentas, sin telemetría. Todo viaja directamente entre tus
  equipos, cifrado con TLS y autenticación mutua.
- **Solo tus equipos**: se vinculan con un código de un solo uso. Lo que copias nunca se guarda ni se
  registra.
- Respeta los gestores de contraseñas: su contenido no se sincroniza.
- Por equipo: enviar y recibir, solo enviar o solo recibir.
- Modo claro y oscuro, inicio con Windows, icono en la bandeja.

**Prueba gratis 14 días** con todas las funciones: descárgala, pruébala y compra la licencia solo si te
sirve. La licencia se activa sin conexión a Internet.

**Requisitos:** Windows 10 (1607+) u 11, o Windows Server 2016+ con Experiencia de escritorio, x64.
Los equipos deben estar en la misma red local.

**Incluye:** instalador de la versión oficial, clave de licencia y actualizaciones. El código fuente es
abierto (GPL-3.0) y está en GitHub.

### English

**Remote Clipboard — Copy on one PC, paste on another.**

Share your clipboard between your Windows computers on the local network: copy text, images or files with
Ctrl+C on one and paste them with Ctrl+V on another. Also works with Windows Server and Remote Desktop.

- **Text** (up to 32 MB), **images** (screenshots, copied pictures) and **files and folders** (up to
  128 MB per copy).
- **Private by design**: no cloud, no accounts, no telemetry. Everything travels directly between your
  computers, encrypted with mutually authenticated TLS.
- **Only your devices**: paired with a one-time code. What you copy is never stored or logged.
- Respects password managers: their content is never synchronized.
- Per device: send and receive, send only or receive only.
- Light and dark mode, starts with Windows, tray icon.

**Free 14-day trial** with every feature. The license activates offline.

**Requirements:** Windows 10 (1607+) or 11, or Windows Server 2016+ with Desktop Experience, x64. The
computers must be on the same local network.

> El instalador aún no está firmado: hasta tener el certificado de firma de código, Windows SmartScreen
> y Chrome muestran un aviso al descargarlo. Mientras tanto, añade a la página: "Windows puede mostrar un
> aviso al instalar: elige *Más información → Ejecutar de todas formas*".

## 4. "Después de comprar" (contenido del producto en Gumroad)

> ¡Gracias por comprar Remote Clipboard!
>
> 1. Descarga e instala `RemoteClipboardSetup` en **todos** los equipos donde lo uses.
> 2. En las próximas horas recibirás **por correo tu clave de licencia** (empieza por `RC1-`).
> 3. En la app: **Acerca de → Licencia** → pega la clave → **Activar**. No necesita Internet.
>
> ¿Dudas o problemas? Responde al correo de la licencia.

## 5. Términos de licencia (para la página o el correo)

> **Licencia de uso de Remote Clipboard (versión oficial)**
>
> 1. La compra te da una clave de licencia para usar la versión oficial de Remote Clipboard:
>    - **Personal**: una persona, en sus propios equipos;
>    - **Empresa**: un usuario de la organización compradora, en los equipos que use para su trabajo.
> 2. La clave es personal: no la publiques ni la compartas fuera de lo que cubre tu edición.
> 3. Incluye las actualizaciones de la app. Si en el futuro hubiera una versión de pago aparte, se
>    anunciará como tal y la licencia actual seguirá sirviendo para las versiones ya publicadas.
> 4. El código fuente de Remote Clipboard se publica bajo la licencia **GPL-3.0**. Nada en estos
>    términos limita los derechos que esa licencia te da sobre el código.
> 5. El software se entrega "tal cual", sin garantías, en la medida en que la ley lo permita.
> 6. © 2026 Marlon Andrés Guerrero Meriño. El nombre y el logotipo de Remote Clipboard no se licencian.

## 6. Política de reembolso

> Puedes probar Remote Clipboard gratis durante 14 días antes de comprar. Si después de comprar no
> funciona en tus equipos y no logramos resolverlo, te devolvemos el dinero dentro de los 14 días
> siguientes a la compra.

En Gumroad los reembolsos se hacen desde **Sales → la venta → Refund**. Si reembolsas, no hace falta
desactivar la clave (no es posible sin servidor); simplemente no le envíes otra.

## 7. Plantilla del correo con la licencia

**Asunto:** Tu licencia de Remote Clipboard

> Hola, {{nombre}}:
>
> ¡Gracias por tu compra! Esta es tu clave de licencia ({{edición}}):
>
> ```
> {{clave RC1-...}}
> ```
>
> Para activarla: abre Remote Clipboard → **Acerca de → Licencia** → pega la clave completa →
> **Activar**. No necesita conexión a Internet.
>
> Guarda este correo: la clave sirve para reinstalar o para activarla en tus otros equipos. Si
> cambias de equipo, usa **Quitar la licencia de este equipo** en el anterior.
>
> Cualquier duda, responde a este correo.
>
> Marlon Guerrero — Remote Clipboard

**Subject:** Your Remote Clipboard license

> Hi {{name}},
>
> Thank you for your purchase! Here is your license key ({{edition}}):
>
> ```
> {{RC1-... key}}
> ```
>
> To activate it: open Remote Clipboard → **About → License** (*Acerca de → Licencia*) → paste the
> whole key → **Activate**. No Internet connection needed.
>
> Keep this e-mail: the key lets you reinstall or activate your other computers.
>
> Marlon Guerrero — Remote Clipboard

## 8. En cada venta

1. Gumroad te avisa por correo con el **nombre**, el **correo** y la **edición** del comprador.
2. En la carpeta privada de tu clave:
   ```
   .\LicenseTool.exe issue --key private-key.pem --to "Nombre del comprador" --edition personal
   ```
   (`--edition business` para Empresa; `--count N` si compró N usuarios).
3. Envía la plantilla de la sección 7 con la clave al correo del comprador.
4. Anota en una hoja de cálculo privada: fecha, comprador, edición e ID de licencia (`L-…`, lo muestra
   `LicenseTool verify`). Te sirve para soporte y reembolsos. No la subas al repositorio.
