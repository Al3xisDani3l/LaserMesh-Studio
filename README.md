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

## Escena, Bluetooth y G-code

- Haz clic derecho sobre una malla para duplicarla, moverla con el inspector o quitarla de la escena. Arrastra con el botón derecho para orbitar.
- En **LP2 · Campo**, ajusta la altura virtual y pulsa **Aplicar altura**. El área nominal es 100 × 100 mm con foco a 110 mm. Fuera de ese plano, el volumen tenue solo estima la cobertura geométrica.
- Pulsa **Elegir cara** y haz clic en la superficie, o selecciona una pieza y pulsa **Cara más alta**. La sección de referencia se mueve con la pieza. La parte del cono que queda por debajo de esa altura se dibuja en naranja transparente.
- Pulsa **Consultar BLE** para buscar `LaserPecker-II103592` con el adaptador de Windows, leer estado y firmware y cerrar la sesión. El nombre se puede editar.
- Abre [el G-code de muestra](samples/cuadro-lp2-simulacion.gcode), reproduce o desplaza el marcador y distingue grabado (ámbar) de desplazamiento (gris). El menú **Grabado** exporta G-code lineal genérico. La trayectoria se conserva como archivo aparte del proyecto. Studio no envía G-code ni arranca grabados.
- **Prueba de posición** reproduce el recorrido en pantalla y muestra sus límites y origen. Con **Seguir pieza**, la proyección se actualiza al aplicar una nueva pose. La cuadrícula usa milímetros y conserva el cero de la escena en el centro del campo.

Las pestañas **Escena**, **Campo LP2**, **Colocación** y **Bluetooth** agrupan los controles. En **Colocación**, selecciona **Contorno exacto** o **Rectángulo envolvente** y pulsa **Generar guía**. El contorno sigue las salientes visibles desde arriba. El rectángulo usa los extremos X/Y. **Exportar guía G-code** guarda las coordenadas de la escena en milímetros con el láser desactivado. La proyección física se habilitará cuando ambos modos estén validados para el LP2.

El escritorio funciona sin dispositivos. El soporte Kinect actual consiste en un cliente IPC y su contrato binario.

## Documentación

- [Arquitectura](docs/ARCHITECTURE.md) y [desarrollo](docs/DEVELOPMENT.md)
- [Pruebas](docs/TESTING.md) y [hoja de ruta](docs/ROADMAP.md)
- [Hardware](docs/HARDWARE.md) e [integración Kinect](docs/KINECT_INTEGRATION.md)
- [Protocolo](docs/LASERPECKER_PROTOCOL.md), [port de PyroGraph](docs/PYROGRAPH_PORTING.md) y [licenciamiento](docs/LICENSE_COMPLIANCE.md)
