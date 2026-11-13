# Licenciamiento y distribución

El código derivado de PyroGraph está identificado con `SPDX-License-Identifier: GPL-3.0-or-later`. Conserva [atribución](../src/LaserMesh.Hardware.LaserPecker/NOTICE.md) y [licencia](../src/LaserMesh.Hardware.LaserPecker/LICENSE) junto al módulo. El submódulo conserva el historial y los avisos originales. Los vectores incluyen capturas históricas de sus pruebas y se documentan con su procedencia.

Studio referencia `LaserMesh.Hardware.LaserPecker.Bluetooth` para las consultas WinRT. Este módulo adapta los perfiles GATT de PyroGraph y referencia el codec derivado. Ambos módulos llevan GPL-3.0-or-later. La distribución del ejecutable integrado requiere resolver los términos del conjunto y la entrega del código fuente correspondiente.

El módulo de protocolo incluye también la conexión y sesión USB de previsualización. `System.IO.Ports` 10.0.8 procede de Microsoft/.NET y usa licencia MIT; se verificaron metadatos NuGet y activos .NET 10 antes de incorporarlo. La sesión USB utiliza los codificadores sujetos a los avisos y términos del módulo GPL. La referencia pública del generador LaserPecker se conserva en `PYROGRAPH_PORTING.md`; no se incorpora código propietario de Design Space.

Antes de publicar una distribución, decidir el licenciamiento de Studio, revisar cómo se enlaza o empaqueta el módulo, conservar avisos y proveer el código fuente correspondiente cuando aplique GPL. Preparar inventario y avisos de dependencias directas y transitivas. Revisar aparte las condiciones de redistribución de los runtimes y drivers de Kinect. La fase actual no establece una licencia de distribución para el resto del producto.
