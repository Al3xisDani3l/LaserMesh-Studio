# Port de PyroGraph

Origen: [nerdocs/pyrograph](https://github.com/nerdocs/pyrograph). Commit fijado: [306f872a49fc347f04396bc2a4be912e6157a814](https://github.com/nerdocs/pyrograph/tree/306f872a49fc347f04396bc2a4be912e6157a814). El checkout vive en `external/pyrograph` como submódulo.

| Archivo analizado | Hallazgo y estado |
| --- | --- |
| `protocol.py` | Portado framing, campos, consultas y parsers. Quedan builders especializados de trabajo, estructura raster y lista larga interpretada. |
| `transport.py` | Portados ensamblado, simulación de consultas y los tres perfiles BLE GATT. WinRT usa el adaptador local de Windows. USB y escritura por lotes siguen pendientes. |
| `device.py` | Analizados la correlación, los acuses espontáneos y la negociación de transferencia. Driver de sesión pendiente. |
| `imaging.py` | Analizados dithering, empaquetado, raster e ID por contenido. Port pendiente. |
| `cli.py` | Revisadas consultas, parámetros de trabajo y separación de ejecución. El port no necesita la CLI. |
| `docs/protocol.md`, `docs/architecture.md`, `docs/connection.md`, `TODO.md`, `LICENSE` | Referencias para layout, transporte, límites de evidencia y atribución. |

Los vectores de salida se generan mediante el `protocol.py` original. Las entradas incluyen capturas de PyroGraph en firmware LP2 3.16 y una trama larga sintética validada con el código original. xUnit compara bytes, endianness y checksum, prueba cada punto de corte de las entradas y verifica tramas consecutivas, truncadas y corruptas. Las pruebas incluyen estado, versión, MAC e identificadores a partir de las capturas.

C# valida el framing antes de interpretar campos, rechaza cuerpos largos menores a dos bytes y limita asignaciones. Campos opcionales se leen solo cuando existen. Los IDs se validan contra el conteo. La simulación solo responde consultas seleccionadas y usa datos en memoria.

Validado el 10 de noviembre de 2026 en Windows con `LaserPecker-II103592`: descubrimiento por nombre exacto, consulta GATT de estado y versión, firmware 3.16, modo 6, error 0 y byte de enlace 1. Se verificó desde un diagnóstico .NET y desde Studio.

Pendiente: recuperación durante desconexiones, compatibilidad con otros firmwares y perfiles, subida, gestión de archivos, movimiento, previsualización y grabado. El método alternativo `0xD0`, algunos estados y la gestión de almacenamiento siguen abiertos en el origen.
