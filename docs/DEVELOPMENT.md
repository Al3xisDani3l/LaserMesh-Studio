# Desarrollo

Requisitos de la aplicación: Windows x64, SDK estable .NET 10 y entorno gráfico DirectX. El destino Windows 10.0.19041.0 incorpora las proyecciones WinRT para BLE. Los módulos de geometría, proyectos, grabado, escaneo y protocolo se prueban con xUnit. El flujo normal de compilación no requiere Kinect SDK, Python ni LaserPecker Design Space.

| Paquete | Versión fijada | Compatibilidad y licencia |
| --- | --- | --- |
| [System.IO.Ports](https://www.nuget.org/packages/System.IO.Ports/10.0.8) | 10.0.8 | Versión estable de Microsoft, activos .NET 10, MIT. Transporte USB serie; E/S síncrona confinada al worker de sesión. |
| [WPF-UI](https://www.nuget.org/packages/WPF-UI/4.3.0) | 4.3.0 | Activos WPF para .NET 10. MIT. |
| [HelixToolkit.Wpf.SharpDX](https://www.nuget.org/packages/HelixToolkit.Wpf.SharpDX/3.1.2) | 3.1.2 | Activos WPF para .NET 8 compatibles con .NET 10. MIT. |
| [CommunityToolkit.Mvvm](https://www.nuget.org/packages/CommunityToolkit.Mvvm/8.4.2) | 8.4.2 | Activos .NET 8 y .NET Standard. MIT. |
| [Hosting](https://www.nuget.org/packages/Microsoft.Extensions.Hosting/10.0.8) y [DependencyInjection](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/10.0.8) | 10.0.8 | Activos .NET 10. MIT. |
| [Serilog.Extensions.Hosting](https://www.nuget.org/packages/Serilog.Extensions.Hosting/10.0.0) | 10.0.0 | Integración para Hosting 10. Apache-2.0. |
| [Serilog.Sinks.File](https://www.nuget.org/packages/Serilog.Sinks.File/7.0.0) | 7.0.0 | Activos .NET 9 y anteriores compatibles. Apache-2.0. |
| [xUnit](https://www.nuget.org/packages/xunit/2.9.3) y [adaptador VS](https://www.nuget.org/packages/xunit.runner.visualstudio/3.1.5) | 2.9.3 y 3.1.5 | Herramientas de prueba compatibles con VSTest en .NET 10. Apache-2.0. |
| [Microsoft.NET.Test.Sdk](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.6.0) | 18.6.0 | Host de pruebas. MIT. |
| [Clipper2](https://www.nuget.org/packages/Clipper2/2.0.0) | 2.0.0 | Unión poligonal en .NET Standard 2.0. Boost Software License 1.0. Su repositorio de origen está archivado. |

Las versiones directas están en `Directory.Packages.props`. Para actualizarlas, revisa activos, licencia, historial del mantenedor, dependencias transitivas, vulnerabilidades y compatibilidad en ejecución. Helix usa una pila DirectX heredada a través de SharpDX. Verifica viewport y picking al actualizarla.

Diagnóstico local: `%LOCALAPPDATA%\LaserMeshStudio\logs`. Los recientes residen en `recent.json` en el mismo directorio de datos. Los logs incluyen rutas locales y conteos para diagnosticar la apertura, importación o escritura.

La integración BLE y G-code se verificó con el SDK global 10.0.403.
