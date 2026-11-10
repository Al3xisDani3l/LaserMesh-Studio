# Kinect V2

## Compatibilidad

[Microsoft.Kinect](https://www.nuget.org/packages/Microsoft.Kinect/2.0.1410.19000) y [Microsoft.Kinect.Fusion.x64](https://www.nuget.org/packages/Microsoft.Kinect.Fusion.x64/2.0.1410.19000) publican activos .NET Framework 4.5. Una restauración separada en `net10.0-windows` con ambos paquetes devolvió `NU1701` para los dos al tratar advertencias como errores. Esto confirma que el uso directo dependería del mecanismo de compatibilidad de NuGet. La ejecución nativa quedó sin verificar. La ficha oficial del SDK enumera sistemas Windows de la época de Kinect v2. Comprueba el runtime y el sensor en el Windows de destino. [SDK 2.0](https://www.microsoft.com/en-us/download/details.aspx?id=44561)

Decisión de arquitectura: auxiliar .NET Framework 4.8 x64, con el SDK y Fusion confinados en ese proceso. La fase actual compila un cliente en .NET 10 y verifica el contrato con un servidor en memoria o Named Pipes de prueba. Queda por implementar el proceso servidor y conectar el sensor.

## Contrato v1

Cada conexión local intercambia una solicitud y su respuesta. Configura un nombre de pipe de sesión y limita ambos extremos al usuario actual. El cliente exige correlación, establece un plazo por operación y cierra el pipe cuando termina o falla.

Cabecera de 16 bytes, con enteros little-endian:

| Offset | Tamaño | Campo |
| --- | --- | --- |
| 0 | 4 | ASCII `LMKB` |
| 4 | 2 | Versión `1` |
| 6 | 2 | Tipo |
| 8 | 4 | ID de solicitud |
| 12 | 4 | Longitud de payload, máximo 8 MiB |

`GetStatus=1` y `CaptureDepth=3` llevan payload vacío. `Status=2` lleva un byte 0/1. `DepthFrame=4` lleva 512 × 424 muestras `UInt16` little-endian en mm, en orden de filas. `Error=255` lleva UTF-8 de hasta 1 KiB. RGB, timestamps, calibración y memoria compartida requieren una ampliación versionada.

## Validación de escaneo

1. Verificar detección y reapertura del dispositivo, captura de profundidad, orientación y unidades.
2. Medir repetibilidad estática contra un artefacto de dimensiones conocidas. Registrar huecos en superficies pequeñas, brillantes o transparentes.
3. Medir eje y registro en una plataforma con ángulo conocido. Separar seguimiento de cámara de movimiento del objeto. Evaluar Fusion con ambos casos antes de adoptar su seguimiento para captura rotatoria.
4. Guardar calibración y métricas de error junto con las capturas. Mantener huecos identificables cuando falten muestras.
