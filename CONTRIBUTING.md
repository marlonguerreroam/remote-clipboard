# Contribuir a Remote Clipboard

¡Gracias por tu interés! Antes de abrir un *pull request*, lee estas condiciones.

## Acuerdo de licencia de contribución (CLA)

Remote Clipboard se publica bajo GPL-3.0-only y el autor ofrece además **licencias comerciales**. Para
poder hacerlo con todo el código, incluido el aportado por otras personas, cada contribución se acepta
solo bajo este acuerdo:

> Al enviar una contribución (código, documentación, traducciones, imágenes u otro material) a este
> repositorio:
>
> 1. **Conservas** los derechos de autor sobre tu contribución.
> 2. Otorgas a **Marlon Andrés Guerrero Meriño** una licencia perpetua, mundial, no exclusiva, gratuita,
>    irrevocable y transferible para usar, reproducir, modificar, distribuir, sublicenciar y
>    **relicenciar** tu contribución bajo cualquier licencia, **incluidas licencias comerciales o
>    propietarias**, y una licencia de patentes con el mismo alcance para las patentes que tu
>    contribución necesariamente infrinja.
> 3. La contribución se publica en este repositorio bajo GPL-3.0-only.
> 4. Declaras que la contribución es **obra tuya** (o que tienes derecho a aportarla en estos términos),
>    que no incluye código de terceros con licencias incompatibles y que tu empleador, si lo hubiera,
>    no reclama derechos sobre ella.
> 5. La contribución se ofrece "tal cual", sin garantías.

Debes aceptarlo marcando la casilla correspondiente en la plantilla del *pull request*. Sin esa
aceptación, la contribución no se integra.

## Reglas técnicas

- Toda la solución compila sin advertencias y pasa las pruebas: `dotnet build` y `dotnet test`.
- Cada archivo fuente empieza con la cabecera de licencia (la compilación lo exige, regla IDE0073):
  ```
  // Copyright (c) 2026 Marlon Andrés Guerrero Meriño
  // SPDX-License-Identifier: GPL-3.0-only
  ```
- **Nunca** registres, guardes ni envíes el contenido del portapapeles. Los eventos de log se definen
  solo en `src/RemoteClipboard.Core/Logging/Log.cs`.
- Sin criptografía propia, sin telemetría, sin servicios externos, sin secretos en el repositorio.
- Commits convencionales (`feat:`, `fix:`, `docs:`, `chore:`, `test:`).
- Nuevas dependencias: solo con licencia compatible con GPL-3.0 y añadidas a
  [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Vulnerabilidades

No abras un *issue* público para fallos de seguridad: sigue la [política de seguridad](.github/SECURITY.md).
