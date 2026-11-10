# Hardware

## Kinect V2

El cliente de escaneo se comunica con un futuro proceso auxiliar x64 mediante Named Pipes locales. Consulta [integración Kinect](KINECT_INTEGRATION.md) para el contrato, las evidencias de compatibilidad y la validación pendiente.

## LaserPecker 2

Studio usa el adaptador BLE de Windows. **Consultar BLE** busca un anuncio por nombre exacto, abre GATT, lee estado y versión y libera la sesión. El 10 de noviembre de 2026, `LaserPecker-II103592` respondió con firmware 3.16, modo 6, error 0 y enlace BLE activo. Las pruebas automáticas del protocolo siguen siendo offline.

### Campo virtual

La guía oficial especifica 100 × 100 mm de área estándar y 110 mm de distancia focal. Studio representa el campo de barrido como un cono de sección cuadrada y estima su lado como `100 × altura / 110`. A 121 mm la base virtual es 110 × 110 mm. El modelo geométrico no calcula potencia, foco, aberración, recorte del escáner ni calidad de grabado. El control de altura modifica solo la escena. [Guía LP2](https://support.laserpecker.net/hc/en-us/articles/7322687094543-LP2-Buying-Guide) · [Enfoque LP2](https://support.laserpecker.net/hc/en-us/articles/7601434598927-LP2-Laser-Focusing-Instructions)

### G-code

Studio importa y exporta G-code lineal genérico. Interpreta G0/G1, G20/G21, G90/G91, X/Y/Z, F, S, M3/M4/M5, M2/M30, comentarios y números de bloque. Empieza en (0,0,0) en mm y modo absoluto. La duración usa el avance programado y supone 3000 mm/min para G0. La exportación reconstruye los segmentos en mm absolutos, preserva el modo de potencia en segmentos activos y agrega M5 antes de desplazamientos rápidos y al final.

Los arcos, offsets, ciclos, interpolación rotatoria y comandos ajenos al subconjunto se rechazan. El formato de transferencia G-code propietario del LP2 requiere evidencia adicional para habilitarlo. [Protocolo documentado por PyroGraph](https://github.com/nerdocs/pyrograph/blob/306f872a49fc347f04396bc2a4be912e6157a814/docs/protocol.md)

El próximo transporte USB debe identificar el puerto explícitamente, respetar timeout y cancelación y filtrar respuestas según el comando en curso. PyroGraph documenta puentes CH340/CH9102 y 460800 baud, 8N1, sin control de flujo. Trátalo como referencia a contrastar en la unidad real. [Conexión según PyroGraph](https://github.com/nerdocs/pyrograph/blob/306f872a49fc347f04396bc2a4be912e6157a814/docs/connection.md)

Requiere acción explícita del operador antes de movimiento, emisión láser o modificación. Mantén identificación, simulación y pruebas de codificación separadas de la prueba física. La primera sesión USB debe limitarse a enumeración, estado y versión. Firmware queda fuera del alcance inicial.
