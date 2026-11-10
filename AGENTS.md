# Convenciones de LaserMesh Studio

- Mantén la aplicación en C#, .NET 10, WPF UI 4.x y HelixToolkit.Wpf.SharpDX 3.x, con destino Windows x64. Usa CommunityToolkit.Mvvm, Microsoft.Extensions.Hosting, DI y Serilog.
- Usa milímetros para geometría interna. Solicita o configura la unidad de origen para formatos sin metadatos de escala. Explicita espacios de escáner, objeto, fixtura y láser antes de componer transformaciones.
- Mantén la malla independiente de WPF y Helix. Las capas de geometría, proyectos, escaneo y protocolo no deben depender de Studio. Crea módulos cuando tengan responsabilidad y código comprobables.
- Mantén tipos anulables, documentación XML en API pública y advertencias de compilación como errores. Usa versiones estables verificadas, con licencia documentada y versiones centralizadas.
- Ejecuta E/S, procesamiento y construcción de índices de picking fuera del Dispatcher. Usa cancelación en operaciones prolongadas y libera recursos de renderizado y hardware.
- Conserva la escritura atómica, recursos embebidos, validación de entradas y versionado de `.lmeshproj`. Evita rutas absolutas dentro del formato.
- Mantén la simulación como modo de desarrollo. Cualquier movimiento, previsualización, grabado o cambio físico requiere una acción explícita del operador. Excluye firmware de la fase inicial.
- Conserva el commit, vectores, licencia y atribución de PyroGraph. Actualiza `docs/PYROGRAPH_PORTING.md` cuando cambie el port. No incorpores código propietario de LaserPecker Design Space.
- Mantén Kinect fuera del proceso WPF. Preserva IPC local versionado, límites de mensajes y validación de correlación. Separa las pruebas de software de la aceptación física.
- Ejecuta build y pruebas Release antes de cerrar cambios. Cuando toques el viewport, prueba visualmente importación, encuadre, picking y restauración en Windows.
