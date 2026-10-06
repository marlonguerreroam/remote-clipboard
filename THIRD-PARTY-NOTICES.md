# Avisos de terceros — Remote Clipboard

Remote Clipboard es © 2026 Marlon Andrés Guerrero Meriño y se distribuye bajo la licencia
**GPL-3.0-only** (ver [LICENSE](LICENSE)).

El ejecutable publicado (instalador y ZIP portable) incluye los siguientes componentes de terceros. Todos
usan la licencia MIT, compatible con la GPL-3.0. Sus textos de licencia completos se distribuyen con la
aplicación en la carpeta `licenses\`.

| Componente | Versión | Licencia | Titular | Texto |
|---|---|---|---|---|
| BouncyCastle.Cryptography (J-PAKE) | 2.7.0 | MIT | The Legion of the Bouncy Castle Inc. | [licenses/BouncyCastle-LICENSE.txt](licenses/BouncyCastle-LICENSE.txt) |
| .NET Runtime y Windows Desktop Runtime (WPF, Windows Forms), incluidos por la publicación *self-contained* | 10.0 | MIT | .NET Foundation and Contributors | [licenses/dotnet-LICENSE.txt](licenses/dotnet-LICENSE.txt) |
| Microsoft.Extensions.Logging y dependencias (`Abstractions`, `DependencyInjection.Abstractions`, `Options`, `Primitives`) | 10.0 | MIT | .NET Foundation and Contributors | [licenses/dotnet-LICENSE.txt](licenses/dotnet-LICENSE.txt) |

El runtime de .NET incorpora a su vez componentes de terceros. Sus avisos, tal como los publica Microsoft
con el runtime, están en [licenses/dotnet-THIRD-PARTY-NOTICES.txt](licenses/dotnet-THIRD-PARTY-NOTICES.txt).

Herramientas usadas solo para compilar (no se distribuyen dentro de la aplicación): .NET SDK, xUnit,
Inno Setup (genera el instalador; su licencia permite distribuir instaladores libremente) y Pillow
(genera el icono).

Al actualizar una dependencia, actualiza esta tabla y copia su texto de licencia en `licenses/`.
