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

Se verificaron byte por byte el inicio y la parada rectangulares contra vectores nuevos de `preview_rect` y `preview_stop`. El simulador en memoria reproduce arranque, consulta, parada y pérdida de enlace. Estas tramas carecen de un camino de envío desde Studio.

La inspección estática sin emisión del paquete local LaserPecker Design Space Beta 3.2.4 encontró referencias a vista de contorno y rectángulo. Esto y el encabezado `0x20` publicado por PyroGraph no confirman una secuencia de previsualización vectorial para firmware LP2 3.16. Las notas oficiales también registran que la configuración de contorno se ocultó para LP2 en LDS Windows 2.10.0. La prueba conjunta solicitada queda pendiente. [Notas oficiales de LDS](https://support.laserpecker.net/hc/en-us/articles/9541633714703-LDS-for-Windows-Release-Notes)

En la revisión previa a la prueba conjunta, el LP2 volvió a responder por BLE con firmware 3.16, modo 6 y error 0. La tabla oficial lista al LP2 en LDS de escritorio y al LX2 en LDS Beta. Las referencias encontradas en el paquete Beta no acreditan soporte para esta unidad. La sesión de diagnóstico se limitó a las consultas de estado y versión. [Compatibilidad oficial de firmware](https://support.laserpecker.net/hc/en-us/articles/9793175374735-Firmware-Compatibility)

## Secuencia de contorno observada en Android

Análisis estático del APK Community 4.36 publicado por LaserPecker, paquete `com.hingin.l1.hiprint`, SHA-256 `C54A1BB6CB83679CEFFC6A07E72A1E02C632BA2D135A66CEB36D1C2449308BC8`. El fabricante lista previsualización de contorno en el LP2. La ruta siguiente es evidencia del cliente Android. Queda por contrastarla con el LP2 objetivo. [APK oficial](https://support.laserpecker.net/hc/en-us/articles/16052037968015-LP1-Pro-Users-Transition-from-LaserPecker-App-to-LaserPecker-Design-Space-App) · [Ficha LP2](https://global.laserpecker.net/products/laserpecker-lp2-classic-handheld-diode-laser-engraver?country=MX&currency=USD&variant=42265155862641)

| Paso | Comportamiento observado |
| --- | --- |
| Anunciar | Función `0x05`, estado `0x01`, longitud total de cuatro bytes big-endian y etiqueta `0x25`. |
| Transferir | Tras el acuse de apertura con la misma etiqueta, pasa al transporte los bytes del archivo. Cabecera de 64 bytes con tipo `0x20`, ID de cuatro bytes en el offset 5 y nombre desde el offset 34. El texto UTF-8 empieza en el offset 64. |
| Cerrar transferencia | Tras acuse de archivo estado `0x02`, resultado `0x01` y etiqueta `0x25`, envía `0xFF` con etiqueta `0x19`. Espera la respuesta de salida y una pausa de 500 ms. |
| Proyectar | Función `0x02`, estado `0x01`, ID de archivo, X/Y de dos bytes cada uno, etiqueta `0x60`, `px=4` para este perfil, nivel de vista previa y diámetro. En modo plano se observa diámetro 0. El nivel predeterminado en el APK es 1. |
| Detener | Al salir de la vista de G-code, el cliente cancela el envío en curso y manda `0xFF` con etiqueta `0x40`. |

El codificador offline C# modela esos campos y sus pruebas verifican formato, checksum y correspondencia del ID. Los vectores se derivan de la inspección, no de una captura del dispositivo. Studio todavía no invoca el codificador por BLE. El cierre de transferencia con `0xFF/0x19` corresponde a esta secuencia de Android. No debe insertarse en el flujo raster de escritorio de PyroGraph.

Para confirmar el flujo en firmware 3.16, la evidencia requerida es una captura ATT de la app móvil con un diseño pequeño, asimétrico y desplazado. Debe cubrir desde la apertura de la vista de contorno hasta su cierre y el regreso a reposo. Registrar versión de la app, dimensiones, desplazamiento y nivel de vista previa. Comparar las escrituras y notificaciones del LP2, incluida la fragmentación del archivo. En Android, habilitar el registro Bluetooth HCI y reiniciar Bluetooth antes de capturar. Exportar únicamente el intercambio del LP2 para el diagnóstico. [Captura Bluetooth HCI en Android](https://source.android.com/docs/core/connect/bluetooth/verifying_debugging?hl=es)

## Diagnóstico directo con LP2 3.16

El diagnóstico BLE confirmó modo 6, error 0 y enlace 1 antes de anunciar un archivo G-code de prueba de 147 bytes. El LP2 respondió `05 01 01 25 00 00` (función, estado, resultado, etiqueta y dos bytes de cola). Esto confirma que aceptó el anuncio. El envío de los datos no recibió el acuse de almacenamiento dentro del plazo. El diagnóstico intentó abortar y una conexión nueva confirmó modo 6 y error 0. Ningún comando de inicio de previsualización ni de grabado salió del diagnóstico.

El reintento con fragmentos de 20 bytes se detuvo en el descubrimiento GATT, antes de enviar el archivo. Tanto el descubrimiento con caché como la consulta de solo lectura posterior devolvieron `Unreachable`. La última lectura válida sigue siendo reposo con error 0. Quedan por comprobar el acuse de almacenamiento y la prueba conjunta de rectángulo y contorno.

## Prueba USB del 13 de noviembre de 2026

COM8 fue identificado como USB-SERIAL CH340, VID 1A86, PID 7523. El diagnóstico C# con System.IO.Ports 10.0.0 usó 460800 baud, 8N1 y sin control de flujo. La consulta respondió firmware 316, modo 6, error 0 y enlace 2.

Se anunció un archivo de 147 bytes, se recibió `05 01 01 25 00 00` y se enviaron cabecera de 64 bytes y texto G-code de 83 bytes. El LP2 confirmó almacenamiento con `05 02 01 25 00 00`. El programa cargado lleva M5 y un recorrido escalonado de 8 × 6 mm. En el flujo USB raw se conservó el archivo tras el acuse, sin enviar el cierre Android de transferencia.

El inicio rectangular de 12 × 8 mm recibió acuse `02 02 00 00 00 00` y la consulta confirmó modo 2, estado 2 y error 0. La parada rectangular devolvió reposo, modo 6 y error 0. El inicio vectorial por ID recibió `02 01 60 00 00 00` y la consulta confirmó modo 2, estado 1 y error 0. La salida `FF 40 00 00 00 00` devolvió reposo, modo 6, estado 255 y error 0.

Ambas secuencias se ejecutaron a nivel de previsualización 1, primero brevemente y después durante ocho segundos por figura. El operador no alcanzó a ver la primera ejecución. La segunda confirmó de nuevo entrada y salida de ambos modos por protocolo. La visibilidad, orientación y dimensiones físicas siguen pendientes de observación del operador. Este diagnóstico se ejecutó desde una consola temporal y todavía no habilita los botones físicos de Studio. Los archivos de prueba permanecen en la memoria del LP2.

### Corrección y observación del operador

El operador observó el primer rectángulo en la esquina superior izquierda y no vio el contorno. Se corrigió el rectángulo de X=0/Y=0 a X=44/Y=46 para situar sus 12 × 8 mm alrededor del centro del campo nominal de 100 × 100 mm. El G-code inicial contenía M5 durante todo el recorrido, una omisión del diagnóstico que podía impedir ver segmentos. Se sustituyó por el dialecto de la referencia del generador LaserPecker, con G1, M03 S1 en el borde y M05 S0 en los desplazamientos y al terminar. El recorrido quedó entre X=46…54 e Y=47…53. El frame de vista previa conservó nivel 1 y estado 0x01. No se envió un comando de inicio de grabado.

La nueva carga tuvo 206 bytes y fue confirmada. El LP2 entró en modo 2 para ambos ensayos de ocho segundos y volvió a modo 6, estado 255 y error 0 al terminar. El operador confirmó que ambas figuras se vieron centradas. Esto acredita visibilidad y centrado cualitativo de esas figuras. La escala física y los signos de ejes con desplazamientos asimétricos aún necesitan medición. La conversión centrada del marco está implementada con el origen de dispositivo en (50,50), X hacia la derecha e Y hacia abajo. [Referencia del generador LaserPecker](https://github.com/yy502/inkscape-laserpecker)

### Integración USB en Studio y encuadre en vivo — 13 de noviembre de 2026

`UsbPreviewConnection` mantiene COM8 a 460800 8N1, sin DTR/RTS ni handshake. `UsbPreviewSession` posee el puerto durante una sesión explícita, verifica firmware 316, enlace USB y reposo, confirma las cargas con etiqueta 0x25 y comprueba modo 2 después de cada inicio. Para reemplazar una guía envía la parada de su modalidad y exige reposo antes del nuevo inicio. Supervisa estado cada segundo; ante cancelación, error, cierre o límite de 60 segundos intenta detener con un plazo independiente de la cancelación. Una parada no confirmada bloquea otros inicios en esa instancia.

`UsbPreviewJob` convierte los perímetros XY de milímetros centrados a coordenadas de dispositivo (50+X, 50−Y). Rectángulo usa el comando rectangular de PyroGraph. Contorno usa el archivo 0x20 y el inicio vectorial documentado arriba, M03 S1 solo sobre perímetros, M05 S0 entre ellos y al terminar. El G-code genérico exportado sigue con el láser apagado. El ID se deriva del contenido G-code completo; no se reutiliza un ID fijo para figuras distintas. La aplicación no elimina otros archivos ni envía inicio de grabado o actualización de firmware.

La interfaz incorpora **Previsualizar en vivo · USB**, parada en pestaña y barra de estado, cambios de modo durante sesión y desplazamiento independiente de encuadre de 1 mm. La generación se ejecuta fuera del Dispatcher, agrupa cambios durante 400 ms y conserva solo la última petición. No abre puertos ni transmite al cambiar la escena sin una sesión explícita.

Se probó en Studio con `seguro_tope_indexado`, 43.5 × 15.8 mm: contorno inicial, desplazamiento X +1 mm, cambio a rectángulo y desplazamiento Y +1 mm. El dispositivo confirmó cada entrada en previsualización; el límite de sesión confirmó reposo al terminar. El operador confirmó que vio ambas guías desplazarse. Esto verifica visibilidad y desplazamiento cualitativos; no sustituye medición de escala, orientación absoluta ni calibración óptica. BLE no se utilizó.

### Corrección de identidad de contornos

El operador detectó que, al sustituir la pieza por `riel_segmento_02`, el visor mostraba la nueva guía pero el LP2 proyectaba la anterior. La implementación inicial enviaba siempre ID 0x4C4D0001. La referencia `packages/laserpecker/src/laserpecker/imaging.py::file_id_for_raster` advierte expresamente que el dispositivo conserva contenido por ID y puede mantener el archivo anterior aunque se vuelva a cargar. El acuse de transferencia y modo 2 no acreditan identidad del contenido proyectado.

Se reproduce en un transporte simulado que conserva la primera carga de cada ID. Dos pruebas nuevas fallaban antes de la corrección. Ahora el ID usa los primeros cuatro bytes big-endian de SHA-256 del G-code UTF-8 completo (cero se remapea a uno). Cabecera e inicio usan el mismo ID. Cambios de forma, pose o desplazamiento que cambien las coordenadas codificadas cambian la identidad; el mismo programa conserva la identidad. La identidad del dispositivo es de 32 bits, por lo que siguen existiendo colisiones teóricas de hash. Cada contenido diferente puede ocupar otra entrada en memoria del LP2; no se implementa borrado automático. La prueba de regresión verifica que la segunda figura iniciada es el contenido nuevo, no solo que se recibe un acuse. Release: 67 pruebas aprobadas.

Se reinició Studio con el proyecto conservado, `riel_segmento_02` a escala 0.5 y guía 59.876 × 59.876 mm, Z 9 mm. La sesión USB confirmó carga e inicio de contorno. El operador confirmó: «Sí, ahora aparece el riel». Al terminar, Studio confirmó parada y equipo en reposo. Esta prueba física verifica la sustitución de la figura almacenada; no mide precisión métrica.
