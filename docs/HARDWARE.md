# Hardware

## Kinect V2

El cliente de escaneo se comunica con un futuro proceso auxiliar x64 mediante Named Pipes locales. Consulta [integración Kinect](KINECT_INTEGRATION.md) para el contrato, las evidencias de compatibilidad y la validación pendiente.

## LaserPecker 2

La primera fase dispone de un codec y de un transporte simulado en memoria para consultas. El módulo está separado de Studio y carece de acceso USB o BLE. Las pruebas comparan vectores offline y respuestas históricas capturadas por PyroGraph.

El próximo transporte USB debe identificar el puerto explícitamente, respetar timeout y cancelación y filtrar respuestas según el comando en curso. PyroGraph documenta puentes CH340/CH9102 y 460800 baud, 8N1, sin control de flujo. Trátalo como referencia a contrastar en la unidad real. [Conexión según PyroGraph](https://github.com/nerdocs/pyrograph/blob/306f872a49fc347f04396bc2a4be912e6157a814/docs/connection.md)

Requiere acción explícita del operador antes de movimiento, emisión láser o modificación. Mantén identificación, simulación y pruebas de codificación separadas de la prueba física. La primera sesión USB debe limitarse a enumeración, estado y versión. Firmware queda fuera del alcance inicial.
