# Pruebas

```powershell
dotnet build LaserMeshStudio.sln -c Release
dotnet test LaserMeshStudio.sln -c Release --no-build
```

La suite verifica geometría y unidades STL, errores de entrada, persistencia y reemplazo atómico, versión del contenedor, tramas LP2, simulación, contrato IPC y comunicación de prueba por Named Pipes. Las pruebas son locales y carecen de acceso a Kinect y al grabador.

Para regenerar los vectores LP2 desde el submódulo limpio y fijado:

```powershell
git submodule update --init
python tests/LaserMesh.Tests/generate_pyrograph_vectors.py
git diff -- tests/LaserMesh.Tests/pyrograph-vectors.json
```

Python se usa únicamente al regenerar los vectores. La suite normal consume el JSON versionado.

## Comprobación visual en Windows x64

1. Abrir Studio sin dispositivos y comprobar tema, cuadrícula, ejes y estado.
2. Importar el tetraedro de muestra en mm. Confirmar dimensiones 40 × 30 × 20 mm, cuatro vértices y cuatro caras.
3. Probar encuadre, selección en viewport, órbita, zoom, pan, alámbrica y cambio de proyección.
4. Aplicar giro y traslación. Guardar, cerrar, abrir el mismo proyecto y comprobar pose y dimensiones.
5. Importar una malla inválida y confirmar que se conserva la escena previa. Probar cancelación con una malla grande.

La CI de GitHub compila y ejecuta las pruebas Release en Windows. La prueba visual y la validación física quedan separadas de CI.

Verificación local en Windows x64 con SDK 10.0.300: Release con 0 advertencias, 0 errores y 34 pruebas aprobadas. NuGet no reportó vulnerabilidades de dependencias directas o transitivas. Los vectores de PyroGraph se regeneraron byte por byte.

En Studio se comprobó arranque sin hardware, importación de la muestra por argumento de inicio, encuadre, vista ortográfica, picking, modo alámbrico, giro de 35°, guardado, cierre y reapertura desde Recientes con el giro conservado. El selector nativo de importación se abrió, pero la automatización del campo de ruta quedó sin verificar. Queda pendiente medir rendimiento con mallas grandes.

## Bluetooth, campo y simulación

La comprobación del 10 de noviembre de 2026 con SDK 10.0.403 pasó 45 pruebas. La suite incluye modos G-code, conversión de unidades, avance, interpolación, exportación y campo geométrico.

En Studio se verificaron la importación y el encuadre STL, el picking con clic derecho, la duplicación, el foco en la traslación desde **Mover objeto**, la restauración del proyecto y su giro de 35°, y el cambio de campo a 110 × 110 mm geométricos con la lente virtual a 121 mm. La muestra G-code dibujó ocho segmentos y el marcador avanzó durante la reproducción.

La comprobación física de BLE se ejecuta por separado con **Consultar BLE**. El equipo `LaserPecker-II103592` contestó estado y versión tanto desde el cliente .NET como desde Studio. No hubo emisión láser ni movimiento.

La verificación de referencia de cara pasó 47 pruebas en Release. En Windows se comprobaron las etiquetas en mm, el cero central, la selección manual y automática de cara, el corte naranja, la prueba virtual y la restauración de un proyecto. Al aplicar +20 mm de traslación Z al tetraedro durante la reproducción, la referencia pasó de Z 8.05 a Z 28.05 mm, y el cono y la trayectoria se actualizaron juntos.

Las guías de colocación pasaron 56 pruebas en Release. En Windows se verificaron importación y encuadre, restauración de la pose, contorno con dos salientes y rectángulo de 27 × 45 mm. Una traslación de +10 mm en X recalculó la guía de X −2…25 a X 8…35 sin cambiar el tamaño. La suite comprueba la ida y vuelta del G-code sin emisión, y el inicio, la parada, la cancelación y la desconexión del transporte rectangular simulado.

## Previsualización USB en vivo — 13 de noviembre de 2026

Release con SDK 10.0.403: 0 advertencias, 0 errores, 65 pruebas aprobadas. Las pruebas USB nuevas usan exclusivamente `IPreviewConnection` simulado: origen y escala de ambos modos, potencia 1 limitada a perímetros, desplazamientos apagados, cambio de guía con parada intermedia, cancelación, firmware incompatible y bloqueo después de desconexión sin reposo confirmado. Ninguna prueba automática abre COM8.

La comprobación manual en Studio mostró el botón **Previsualizar en vivo · USB** habilitado, contorno sobre malla con dos salientes y controles X/Y de 1 mm. En COM8 se comprobaron inicio de contorno, actualización X +1 mm, cambio a rectángulo, actualización Y +1 mm y parada automática a 60 segundos con reposo confirmado. El operador confirmó visibilidad y desplazamiento de ambas guías. La observación no acredita precisión métrica ni orientación absoluta. Un reporte posterior descubrió que el ID fijo podía conservar contenido anterior; esa observación inicial no prueba que las coordenadas nuevas se hubieran cargado correctamente.

La corrección de caché por ID pasa 67 pruebas Release, sin advertencias ni errores. Las dos regresiones reproducen la conservación del primer contenido por ID y verifican que un cambio de figura o traslación cambia el ID usado en carga e inicio. Ambas fallaban con la implementación de ID fijo y pasan con identidad derivada del programa.

Se recompiló y reinició el ejecutable habitual, conservando `riel_segmento_02` con escala 0.5 en un proyecto local. El operador confirmó que ahora aparece el riel curvo durante la previsualización USB; Studio confirmó reposo al terminar. No se enviaron comandos de grabado.
