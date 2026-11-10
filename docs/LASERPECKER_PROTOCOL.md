# Protocolo LaserPecker

Fuente de referencia: [protocolo PyroGraph fijado](https://github.com/nerdocs/pyrograph/blob/306f872a49fc347f04396bc2a4be912e6157a814/docs/protocol.md).

- Una trama corta empieza en `AA BB`. Longitud de un byte, función de un byte, payload y checksum de dos bytes. La longitud cuenta función, payload y checksum. El codificador limita el payload a 249 bytes como PyroGraph.
- Una respuesta larga empieza en `AA CC`. Longitud de cuatro bytes big-endian, payload sin byte de función y checksum de dos bytes. El límite local de cuerpo es 1 MiB. El payload de la lista de nombres queda opaco.
- El checksum es la suma de los bytes del cuerpo anteriores al checksum, módulo 65536, en big-endian. Incluye función en tramas cortas.
- El ensamblador conserva fragmentos, separa tramas consecutivas y descarta prefijos inválidos y candidatos con checksum incorrecto. Una longitud plausible pero incompleta requiere timeout y reinicio del ensamblador.
- Los parsers de estado, versión, MAC, nombre, IDs y acuse de archivo reciben una trama previamente validada. La futura sesión deberá asociar respuestas a su solicitud y separar acuses espontáneos.

Solo se exponen builders específicos de consulta. El codificador genérico permite formar bytes en memoria. No existe un camino de envío físico.
