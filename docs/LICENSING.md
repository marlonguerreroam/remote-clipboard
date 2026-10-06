# Licencias y venta directa

Remote Clipboard se vende directamente (Windows 10/11 y Windows Server) con **claves de licencia que se
validan sin conexión**: la app nunca contacta un servidor para comprobarlas. En la versión de la
Microsoft Store no hay claves: la Store cobra la compra.

## Cómo funciona

- **Prueba gratuita de 14 días** desde el primer arranque, con todas las funciones. Al terminar sin
  licencia solo se desactiva la **sincronización**; los equipos vinculados y la configuración se
  conservan.
- **Clave de licencia**: `RC1-<datos>.<firma>`. Los datos (identificador, nombre del cliente, edición
  personal/empresarial y fecha) van firmados con **ECDSA P-256 / SHA-256** (criptografía estándar de
  .NET). La app solo contiene la **clave pública**: puede comprobar una clave, pero no crearla.
- La clave se activa en **Acerca de → Licencia** y se guarda cifrada con DPAPI del usuario. Se puede
  quitar para usarla en otro equipo.
- Mientras `LicensingConfig.PublicKey` esté vacía (por ejemplo, en una copia compilada por otra persona),
  no hay prueba ni licencias.

## Lo que te toca hacer (una sola vez)

1. Descarga `LicenseTool.exe` del borrador privado de *Releases* (`prueba-…` o `vX.Y.Z-binarios`).
2. En una carpeta **privada** de tu PC, abre una terminal y ejecuta:
   ```
   LicenseTool.exe keygen
   ```
   Elige una contraseña de al menos 12 caracteres. Se crea `private-key.pem` (cifrada con esa
   contraseña) y se muestra la **clave pública**.
3. **Haz copia de seguridad** de `private-key.pem` y de la contraseña (por ejemplo, en un gestor de
   contraseñas y una memoria USB). Si la pierdes, no podrás emitir más licencias para esa clave pública;
   si se filtra, cualquiera podría crear licencias.
4. Pásame la **clave pública** (la línea larga en base64; no es secreta). La incluyo en
   `LicensingConfig.PublicKey` junto con la URL de tu página de compra (`PurchaseUrl`).

**Nunca** subas `private-key.pem` a GitHub ni la envíes por chat o correo. El repositorio ignora los
archivos `*.pem`, pero lo importante es no copiarla fuera de tu equipo y tus copias de seguridad.

## Emitir licencias

Por cada venta:
```
LicenseTool.exe issue --key private-key.pem --to "Nombre del cliente" --edition personal
```
Usa `--edition business` para empresas y `--count N` para generar varias claves a la vez. Para
comprobar una clave: `LicenseTool.exe verify --public <clave pública> --license <RC1-…>`.

### Con la plataforma de pago

Plataformas como **Lemon Squeezy**, **Paddle** o **FastSpring** actúan como vendedor oficial: cobran y
gestionan facturas e impuestos de cada país. Para entregar la clave:

- **Al principio (pocas ventas)**: la plataforma te notifica la compra y tú envías la clave generada con
  `issue` al correo del cliente.
- **Automático**: algunas plataformas permiten cargar una lista de claves pregeneradas que entregan al
  comprador (genera un lote con `--count` y un nombre genérico, por ejemplo `--to "Cliente"`). Revisa en
  la plataforma elegida si lo admite. Las licencias propias de la plataforma que se validan contra su
  servidor **no** son compatibles con la promesa "sin Internet" de la app.

## Límites (con honestidad)

- El código es GPL-3.0 y público: alguien con conocimientos puede compilar una copia sin la comprobación
  de licencia. Las licencias protegen la versión oficial, firmada y con soporte, que es la que compra la
  mayoría de la gente.
- La prueba se guarda en el perfil del usuario; borrar los datos de la app reinicia la prueba, pero
  también la identidad del equipo y sus vinculaciones.
- No se limita el número de equipos por licencia (sería imposible sin un servidor). Indícalo en los
  términos de venta (por ejemplo, "uso personal en tus equipos" o "por usuario en empresas").
