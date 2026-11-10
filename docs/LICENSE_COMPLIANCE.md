# Licenciamiento y distribución

El código derivado de PyroGraph está identificado con `SPDX-License-Identifier: GPL-3.0-or-later`. Conserva [atribución](../src/LaserMesh.Hardware.LaserPecker/NOTICE.md) y [licencia](../src/LaserMesh.Hardware.LaserPecker/LICENSE) junto al módulo. El submódulo conserva el historial y los avisos originales. Los vectores incluyen capturas históricas de sus pruebas y se documentan con su procedencia.

Studio referencia `LaserMesh.Hardware.LaserPecker.Bluetooth` para las consultas WinRT. Este módulo adapta los perfiles GATT de PyroGraph y referencia el codec derivado. Ambos módulos llevan GPL-3.0-or-later. La distribución del ejecutable integrado requiere resolver los términos del conjunto y la entrega del código fuente correspondiente.

Antes de publicar una distribución, decidir el licenciamiento de Studio, revisar cómo se enlaza o empaqueta el módulo, conservar avisos y proveer el código fuente correspondiente cuando aplique GPL. Preparar inventario y avisos de dependencias directas y transitivas. Revisar aparte las condiciones de redistribución de los runtimes y drivers de Kinect. La fase actual no establece una licencia de distribución para el resto del producto.
