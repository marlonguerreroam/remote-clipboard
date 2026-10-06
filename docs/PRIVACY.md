# Política de privacidad — Remote Clipboard

*Última actualización: 6 de octubre de 2026 · Responsable: Marlon Andrés Guerrero Meriño
([@marlonguerreroam](https://github.com/marlonguerreroam))*

Remote Clipboard sincroniza el portapapeles de texto entre equipos Windows **que tú vinculas
explícitamente**, dentro de tu red local. Esta política explica qué datos maneja y cómo.

## Qué datos maneja la aplicación

| Dato | Uso | Dónde queda |
|---|---|---|
| **Texto que copias** (portapapeles) | Se envía a tus equipos vinculados para que puedas pegarlo allí. | Solo en memoria, mientras se transmite. **No se guarda** en disco, **no se registra** en los logs y no hay historial. |
| Nombre del equipo, versión de Windows, direcciones IP de la red local | Mostrarte qué equipos están conectados y encontrarlos en tu red. | Tu equipo y tus equipos vinculados. |
| Identidad del dispositivo (identificador aleatorio y clave criptográfica) | Autenticar a tus equipos vinculados. | Tu equipo, cifrada con la protección de datos de Windows (DPAPI) de tu usuario. |
| Lista de equipos vinculados y preferencias | Funcionamiento de la aplicación. | Tu equipo. |
| Registros técnicos (logs) | Diagnosticar problemas. Nunca contienen el texto copiado ni códigos de vinculación. | Tu equipo, 7 días como máximo. |

## Lo que la aplicación NO hace

- **No envía nada a Internet.** No hay servidores, nube, cuentas, publicidad, analítica ni telemetría.
  Todo el tráfico va directamente entre tus equipos de la red local.
- **No comparte datos con terceros**, ni con el desarrollador.
- **No sincroniza** el contenido que los gestores de contraseñas marcan como excluido, y lo que recibe de
  otro equipo no va al historial ni al portapapeles en la nube de Windows.

## Seguridad

Todo el tráfico entre equipos va cifrado con TLS y autenticación mutua. Solo los equipos que vinculaste
con un código de un solo uso pueden enviar o recibir contenido. Detalles técnicos en
[SECURITY.md](SECURITY.md).

## Tus opciones

- Pausar la sincronización en cualquier momento (interruptor de la ventana o menú de la bandeja).
- Elegir por cada equipo: enviar y recibir, solo enviar o solo recibir.
- Desvincular un equipo, que dejará de recibir o enviar contenido.
- Desinstalar la aplicación. La versión de la Microsoft Store borra también sus datos locales; en la
  versión clásica puedes borrar `%LOCALAPPDATA%\RemoteClipboard`.

## Menores

La aplicación no está dirigida a menores ni recopila datos de ninguna persona.

## Cambios y contacto

Los cambios de esta política se publican en este archivo, con su historial en el repositorio. Para
consultas, abre un *issue* en [el repositorio](https://github.com/marlonguerreroam/remote-clipboard) o
escribe a través del perfil de GitHub del responsable.
