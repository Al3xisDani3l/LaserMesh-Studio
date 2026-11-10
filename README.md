# LaserMesh Studio

Aplicación CAD/CAM para Windows x64. La primera fase permite importar mallas STL, inspeccionarlas en un viewport DirectX y guardarlas con sus transformaciones en un proyecto local.

## Ejecutar

Instala el SDK de .NET 10 x64. Ejecuta desde la raíz del repositorio:

```powershell
git submodule update --init
dotnet build LaserMeshStudio.sln -c Release
dotnet test LaserMeshStudio.sln -c Release --no-build
dotnet run --project src/LaserMesh.Studio -c Release --no-build
```

1. Selecciona la unidad de origen junto a **Importar STL**. STL carece de metadatos de unidades. Prueba con [el tetraedro de muestra](samples/tetraedro-40x30x20.stl) en mm.
2. Selecciona la malla en el explorador o en el viewport. El inspector muestra dimensiones locales, límites, centroide, vértices distintos y caras. Edita traslación, giro Z o escala uniforme y pulsa **Aplicar**.
3. Usa **Guardar** para crear un `.lmeshproj`. Ábrelo desde el menú Proyecto para recuperar los recursos embebidos y sus transformaciones.

La rueda aplica zoom, el botón derecho orbita y Mayús + botón derecho desplaza la vista. **Ver** incluye encuadre, proyecciones, vistas superior y frontal y malla alámbrica.

También puedes abrir un proyecto pasando su ruta como argumento. Para un STL, indica explícitamente su unidad:

```powershell
dotnet run --project src/LaserMesh.Studio -c Release --no-build -- --stl samples/tetraedro-40x30x20.stl mm
```

El escritorio funciona sin dispositivos. El protocolo LP2 vive en un módulo independiente con simulación en memoria. El soporte Kinect actual consiste en un cliente IPC y su contrato binario.

## Documentación

- [Arquitectura](docs/ARCHITECTURE.md) y [desarrollo](docs/DEVELOPMENT.md)
- [Pruebas](docs/TESTING.md) y [hoja de ruta](docs/ROADMAP.md)
- [Hardware](docs/HARDWARE.md) e [integración Kinect](docs/KINECT_INTEGRATION.md)
- [Protocolo](docs/LASERPECKER_PROTOCOL.md), [port de PyroGraph](docs/PYROGRAPH_PORTING.md) y [licenciamiento](docs/LICENSE_COMPLIANCE.md)
