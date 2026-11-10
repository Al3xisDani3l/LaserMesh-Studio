# Licenciamiento y distribución

El código derivado de PyroGraph está identificado con `SPDX-License-Identifier: GPL-3.0-or-later`. Conserva [atribución](../src/LaserMesh.Hardware.LaserPecker/NOTICE.md) y [licencia](../src/LaserMesh.Hardware.LaserPecker/LICENSE) junto al módulo. El submódulo conserva el historial y los avisos originales. Los vectores incluyen capturas históricas de sus pruebas y se documentan con su procedencia.

Estrategia de desarrollo: mantener el módulo derivado aislado y sin referencia desde el ejecutable Studio. Esta organización facilita la revisión de distribución. La separación de proyectos por sí sola no determina las obligaciones de una integración o distribución futura.

Antes de publicar una distribución, decidir el licenciamiento de Studio, revisar cómo se enlaza o empaqueta el módulo, conservar avisos y proveer el código fuente correspondiente cuando aplique GPL. Preparar inventario y avisos de dependencias directas y transitivas. Revisar aparte las condiciones de redistribución de los runtimes y drivers de Kinect. La fase actual no establece una licencia de distribución para el resto del producto.
