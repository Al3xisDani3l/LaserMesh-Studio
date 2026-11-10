# Desarrollo

Requisitos de la aplicación: Windows x64, SDK estable .NET 10 y entorno gráfico DirectX. Los módulos de geometría, proyectos, escaneo y protocolo se prueban con xUnit. El flujo normal de compilación no requiere Kinect SDK, Python ni LaserPecker Design Space.

| Paquete | Versión fijada | Compatibilidad y licencia |
| --- | --- | --- |
| [WPF-UI](https://www.nuget.org/packages/WPF-UI/4.3.0) | 4.3.0 | Activos WPF para .NET 10. MIT. |
| [HelixToolkit.Wpf.SharpDX](https://www.nuget.org/packages/HelixToolkit.Wpf.SharpDX/3.1.2) | 3.1.2 | Activos WPF para .NET 8 compatibles con .NET 10. MIT. |
| [CommunityToolkit.Mvvm](https://www.nuget.org/packages/CommunityToolkit.Mvvm/8.4.2) | 8.4.2 | Activos .NET 8 y .NET Standard. MIT. |
| [Hosting](https://www.nuget.org/packages/Microsoft.Extensions.Hosting/10.0.8) y [DependencyInjection](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/10.0.8) | 10.0.8 | Activos .NET 10. MIT. |
| [Serilog.Extensions.Hosting](https://www.nuget.org/packages/Serilog.Extensions.Hosting/10.0.0) | 10.0.0 | Integración para Hosting 10. Apache-2.0. |
| [Serilog.Sinks.File](https://www.nuget.org/packages/Serilog.Sinks.File/7.0.0) | 7.0.0 | Activos .NET 9 y anteriores compatibles. Apache-2.0. |
| [xUnit](https://www.nuget.org/packages/xunit/2.9.3) y [adaptador VS](https://www.nuget.org/packages/xunit.runner.visualstudio/3.1.5) | 2.9.3 y 3.1.5 | Herramientas de prueba compatibles con VSTest en .NET 10. Apache-2.0. |
| [Microsoft.NET.Test.Sdk](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.6.0) | 18.6.0 | Host de pruebas. MIT. |

Las versiones directas están en `Directory.Packages.props`. Para actualizarlas, revisa activos, licencia, historial del mantenedor, dependencias transitivas, vulnerabilidades y compatibilidad en ejecución. Helix usa una pila DirectX heredada a través de SharpDX. Verifica viewport y picking al actualizarla.

Diagnóstico local: `%LOCALAPPDATA%\LaserMeshStudio\logs`. Los recientes residen en `recent.json` en el mismo directorio de datos. Los logs incluyen rutas locales y conteos para diagnosticar la apertura, importación o escritura.

En esta estación se verificó con una copia portátil del SDK 10.0.300, validada por SHA-512 a partir del catálogo oficial. El SDK global reportaba la versión pero carecía de `sdk\10.0.300\dotnet.dll`. Usa el SDK global una vez que vuelva a estar operativo o ejecuta el `dotnet.exe` de la copia portátil en `%LOCALAPPDATA%\LaserMeshStudio\verification-sdk-10.0.300`.
