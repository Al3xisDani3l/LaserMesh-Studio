# Hoja de ruta

1. **Base de escritorio.** STL en unidades configurables, vista 3D, inspector, transformaciones y proyectos embebidos. Núcleo binario LP2, simulación de consultas y cliente IPC Kinect.
2. **Integración de lectura.** Captura de profundidad mediante auxiliar Kinect x64 y verificación estática en Windows de destino. En paralelo, conectar LP2 por USB con operaciones de solo lectura y política explícita de timeout y reconexión.
3. **Activos 3D.** Importación OBJ/PLY, exportación STL/OBJ/PLY, biblioteca, perfiles de calibración, reconstrucción estática y métricas. Evaluar GLTF/GLB y 3MF después.
4. **Diseño y movimiento.** Proyección planar y cilíndrica, registro de eje rotatorio y perfiles de láser. Validar foco y geometría con medidas. Añadir preparación raster y sesión de transferencia con simulación.
5. **Grabado controlado.** Validar trabajos acotados, cancelación y acciones del operador. Añadir BLE después de USB y ampliar el conjunto de formatos y modos solo con casos verificados.
