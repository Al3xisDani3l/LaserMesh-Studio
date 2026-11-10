# Hardware

## Kinect V2

El cliente de escaneo se comunica con un futuro proceso auxiliar x64 mediante Named Pipes locales. Consulta [integración Kinect](KINECT_INTEGRATION.md) para el contrato, las evidencias de compatibilidad y la validación pendiente.

## LaserPecker 2

Studio usa el adaptador BLE de Windows. **Consultar BLE** busca un anuncio por nombre exacto, abre GATT, lee estado y versión y libera la sesión. El 10 de noviembre de 2026, `LaserPecker-II103592` respondió con firmware 3.16, modo 6, error 0 y enlace BLE activo. Las pruebas automáticas del protocolo siguen siendo offline.

### Campo virtual

La guía oficial especifica 100 × 100 mm de área estándar y 110 mm de distancia focal. Studio representa el campo de barrido como un cono de sección cuadrada y estima su lado como `100 × altura / 110`. A 121 mm la base virtual es 110 × 110 mm. El modelo geométrico no calcula potencia, foco, aberración, recorte del escáner ni calidad de grabado. El control de altura modifica solo la escena. [Guía LP2](https://support.laserpecker.net/hc/en-us/articles/7322687094543-LP2-Buying-Guide) · [Enfoque LP2](https://support.laserpecker.net/hc/en-us/articles/7601434598927-LP2-Laser-Focusing-Instructions)

### G-code

Studio importa y exporta G-code lineal genérico. Interpreta G0/G1, G20/G21, G90/G91, X/Y/Z, F, S, M3/M4/M5, M2/M30, comentarios y números de bloque. Empieza en (0,0,0) en mm y modo absoluto. La duración usa el avance programado y supone 3000 mm/min para G0. La exportación reconstruye los segmentos en mm absolutos, preserva el modo de potencia en segmentos activos y agrega M5 antes de desplazamientos rápidos y al final.

La cuadrícula tiene su cero en la intersección del eje óptico virtual con Z = 0. Al elegir una cara, el punto pulsado pasa a ser el cero de la proyección visual. **Cara más alta** usa el centroide del triángulo proyectable de mayor altura. Las caras casi verticales quedan fuera de esta proyección.

El cono conserva su vértice en la lente virtual. Desde la lente hasta la altura de referencia mantiene el morado y desde esa sección hasta Z = 0 muestra naranja transparente. Studio muestra la distancia entre lente y cara. La parte naranja es una extrapolación más allá de la altura elegida.

**Prueba de posición** superpone origen, límites XY y recorrido animado sin emisión. **Seguir pieza** actualiza el plano de proyección con la pose editada en Studio. Las dimensiones XY programadas siguen en milímetros y la proyección se extiende al plano de la cara. No se recorta al contorno de la malla. Esta colocación es de sesión y no cambia el G-code exportado. La previsualización rectangular física descrita por PyroGraph enciende el láser y no representa el recorrido arbitrario de este G-code.

Los arcos, offsets, ciclos, interpolación rotatoria y comandos ajenos al subconjunto se rechazan. El formato de transferencia G-code propietario del LP2 requiere evidencia adicional para habilitarlo. [Protocolo documentado por PyroGraph](https://github.com/nerdocs/pyrograph/blob/306f872a49fc347f04396bc2a4be912e6157a814/docs/protocol.md)

### Guías de colocación

El contorno une triángulos proyectados desde arriba. Conserva los perímetros exteriores de las componentes separadas y omite agujeros. El rectángulo se alinea con XY y usa las dimensiones máximas de la pieza ya transformada. La cota de la cara más alta solo eleva la guía visual. La exportación G-code guarda su XY absoluto, Z = 0 y movimientos sin emisión. Los cambios de pieza, pose o modo regeneran una guía activa localmente.

La primera prueba física está condicionada a validar los dos modos para el mismo LP2. El marco rectangular solo tiene paridad de bytes con PyroGraph y simulación local. El contorno libre sigue pendiente de verificar en firmware 3.16. Antes de habilitar ambos, se requieren una captura o un ensayo sin emisión que confirme coordenadas, alimentación de geometría, inicio y parada. En la primera prueba física se requiere operador presente, protección cerrada, nivel mínimo verificado y confirmación de reposo entre modos. Una pérdida de enlace durante una proyección futura requiere tratar el estado del láser como desconocido hasta verificar la parada.

El próximo transporte USB debe identificar el puerto explícitamente, respetar timeout y cancelación y filtrar respuestas según el comando en curso. PyroGraph documenta puentes CH340/CH9102 y 460800 baud, 8N1, sin control de flujo. Trátalo como referencia a contrastar en la unidad real. [Conexión según PyroGraph](https://github.com/nerdocs/pyrograph/blob/306f872a49fc347f04396bc2a4be912e6157a814/docs/connection.md)

Requiere acción explícita del operador antes de movimiento, emisión láser o modificación. Mantén identificación, simulación y pruebas de codificación separadas de la prueba física. La primera sesión USB debe limitarse a enumeración, estado y versión. Firmware queda fuera del alcance inicial.
