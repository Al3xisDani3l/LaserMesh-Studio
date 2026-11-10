# Arquitectura

| Módulo | Responsabilidad |
| --- | --- |
| `LaserMesh.Studio` | Shell WPF, comandos MVVM, diálogos, inspector, recientes y composición mediante DI. |
| `LaserMesh.Geometry` | Malla inmutable en mm, codec STL y estadísticas. |
| `LaserMesh.Rendering` | Conversión a buffers Helix, octree de picking, cuadrícula y ejes. |
| `LaserMesh.Projects` | Modelos del proyecto, poses y almacenamiento versionado. |
| `LaserMesh.Scanning` | Cliente Named Pipes y contratos binarios de Kinect. |
| `LaserMesh.Hardware.LaserPecker` | Framing, respuestas y simulación local. |

Studio usa Geometry, Projects y Rendering. Projects y Rendering usan Geometry. Scanning y Hardware.LaserPecker se construyen y prueban de forma independiente. El escritorio puede iniciar aunque falten SDK, drivers o dispositivos.

El STL se analiza en un hilo de trabajo. Geometry usa `System.Numerics.Vector3` y recalcula normales planas según el orden de vértices. Rendering prepara buffers e índices de picking fuera del Dispatcher. La creación y vinculación de los elementos WPF ocurre en el Dispatcher.

El espacio de objeto usa coordenadas en milímetros. La pose transforma en este orden: escala uniforme, giro sobre +Z y traslación. Las estadísticas del inspector pertenecen a la geometría local. El encuadre usa límites transformados. El centroide usa área superficial como ponderación y no requiere malla cerrada.

## Proyecto v1

`.lmeshproj` contiene un ZIP con `manifest.json` y `meshes/{id:N}.stl` en STL binario. El manifest incluye `Version`, `Unit` y `Models`. Cada modelo contiene `Id`, `Name` y `Pose`. Las rutas de recursos se derivan del identificador.

El guardado escribe en un archivo temporal del mismo directorio, vacía el flujo a disco y reemplaza el destino mediante renombrado. El lector valida versión, unidades, poses, identificadores, entradas duplicadas y tamaños. Solo aplica una escena al completar la lectura y la preparación de sus mallas. El formato permite hasta 128 modelos y dos millones de caras por proyecto. El límite por STL es un millón de caras y 256 MiB de entrada.

Diseños, imágenes, capas de trabajo, biblioteca de activos, escaneos y calibraciones ampliarán el formato cuando existan sus modelos de dominio.
