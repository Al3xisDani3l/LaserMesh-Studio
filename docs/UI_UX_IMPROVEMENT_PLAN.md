# LaserMesh Studio — propuesta de experiencia y diseño

Fecha: 13 de noviembre de 2026. Estado: especificación para planificar; funcionalidades propuestas, salvo donde se describa evidencia actual. Plataforma: Windows x64, .NET 10, WPF UI 4.x y HelixToolkit 3.x. Entregable de investigación, no implementación de la interfaz.

## 1. Resultado buscado

Convertir Studio en una herramienta de composición 3D y preparación de grabado: importar varias piezas, seleccionarlas, colocarlas directamente, asignar diseños a superficies y revisar una guía coherente con todo el conjunto. El usuario debe comprender qué está seleccionado, qué modifica, qué se proyectará y si el dispositivo está emitiendo. La escena responde inmediatamente; la máquina responde solo dentro de una sesión física iniciada explícitamente.

Prioridad: corregir interacción y modelo de selección antes de decorar. Una paleta moderna no resuelve selección individual, valores que requieren Aplicar ni guías con alcance ambiguo. Conservar la aplicación utilizable sin dispositivo y sin Kinect.

## 2. Evidencia y diagnóstico

Se revisaron la captura aportada, XAML, picking, estado del inspector, importación, generación de guías, referencia de cara y sesión USB. No se realizó una nueva prueba física para este documento.

La captura muestra `riel_segmento_02` y `seguro_tope_indexado` cruzados y con alturas distintas. Solo el riel aparece seleccionado en el explorador; el borde amarillo corresponde a esa pieza. El inspector usa escala 0.5, pero titula sus dimensiones locales 119.75 × 119.75 × 18: falta mostrar también dimensiones resultantes. La ventana dedica varias filas a menús, pestañas, ajustes y acciones; el viewport queda subordinado a controles de igual peso visual.

| Hallazgo | Evidencia actual | Consecuencia | Corrección propuesta |
|---|---|---|---|
| Selección única | `MainWindow.xaml`: `SelectedItem` de ListBox; `MainWindow.xaml.cs::OnPick` sustituye `Selected` | Ctrl+clic no acumula piezas | Servicio de selección compartido entre escena, árbol e inspector |
| Sin movimiento directo de objetos | `OnPick` hace picking; no inicia una herramienta de transformación | El usuario depende del inspector | Arrastre XY y gizmos de traslado, giro y escala |
| Sin historial de operaciones de escena | Comandos actuales modifican modelos directamente; no hay pila Undo/Redo de escena | Cada error exige reconstrucción manual | Transacciones reversibles, agrupadas por gesto |
| Importación secuencial | `StudioViewModel::ImportAsync` usa un `OpenFileDialog` sin multiselección | Varios STL requieren repetir diálogo | Multiselección y arrastre de archivos por lote |
| Edición diferida | `ApplyPose`, campos de texto y botón Aplicar | Más clics; resultado no coincide con el valor que se está escribiendo | Edición numérica inmediata con validación local |
| Guía de una pieza | `MainWindow.Placement.cs` usa `viewModel.Selected.Model`; `PlacementGuide.Create` recibe una malla | La segunda pieza cruzada queda fuera | Alcance explícito y operaciones sobre colecciones |
| Altura y geometría se confunden | `PlacementGuide` une todos los triángulos proyectados XY; la cara alta fija Z de dibujo en `MainWindow.Placement.cs` | Parece que se selecciona una cara, pero se calcula otra cosa | Separar alcance, tipo geométrico y referencia Z |
| Referencia manual no gobierna esa guía | `MainWindow.SurfaceReference.cs` conserva `faceReference`; la guía calcula su propia cara alta | Elegir una cara no garantiza el plano esperado | Contrato común de referencia para campo, guía y diseño |
| Menú contextual básico | Duplicar, mover desde inspector y quitar de escena | Operaciones frecuentes están dispersas | Menú por contexto y selección múltiple |
| Lectura espacial insuficiente | Captura: etiquetas sueltas cada 40 mm, sin reglas continuas | Cuesta estimar tamaños y posiciones | Reglas de pantalla, retícula adaptativa y cotas de selección |
| Estado del hardware ambiguo | Sesión USB existe, pero la barra también puede decir BLE sin consultar/simulación | Puede interpretarse simulación mientras hay proyección USB | Un estado principal de dispositivo y una sesión física inequívoca |

**Corrección conceptual importante:** el problema observado no demuestra que el algoritmo extraiga solo la cara superior. En la versión examinada calcula la silueta completa de **una malla**. Para las dos piezas de la captura se necesitan selección múltiple y alcance de conjunto; para obtener un corte inferior se necesita otro algoritmo geométrico.

## 3. Principios de interacción

1. Ver el resultado antes de confirmar una acción física. La edición normal no requiere un botón Aplicar.
2. Una selección coherente en árbol, viewport, inspector y guía; siempre indicar cantidad y objeto activo.
3. Acciones frecuentes a un gesto; opciones avanzadas a un nivel de profundidad.
4. Deshacer recupera trabajo; Escape cancela el gesto actual. Ninguno reinicia una emisión física.
5. Los mismos comandos sirven a menú, teclado, barra y contexto; no duplicar comportamiento.
6. Unidades y espacios explícitos. Separar coordenadas de objeto, escena, soporte y láser.
7. Las ayudas de precisión acompañan al cursor; no saturan todo el viewport.
8. Accesibilidad por teclado y alternativas al arrastre. El ratón no debe ser la única forma de completar una tarea.

## 4. Estructura de la ventana

Inspiración de Visual Studio: jerarquía estable de comandos, herramientas contextualizadas, paneles acoplables y búsqueda de comandos. No copiar toda su complejidad ni afirmar equivalencia con una versión concreta.

```text
Título · proyecto · cambios pendientes                         dispositivo USB
Archivo  Editar  Ver  Escena  Diseño  Preparación  Herramientas   Buscar comandos
Importar | Guardar | Deshacer Rehacer | Seleccionar Mover Girar Escalar | Ajustes
Contexto: 2 piezas · pivote conjunto · XY · paso 1 mm       Previsualizar ▾ Detener
┌ Explorador ┐┌ regla X / vista / herramientas de cámara ┐┌ Inspector contextual ┐
│ Modelos   ││                                         ││ Selección / pose      │
│ Diseños   ││       viewport 3D, foco del trabajo       ││ Diseño / proyección   │
│ Guías     ││ regla Y · retícula · cotas · manipulador  ││ Propiedades avanzadas │
└───────────┘└─────────────────────────────────────────┘└──────────────────────┘
Estado: 2 seleccionadas · guía actualizada · mm         USB reposo / Proyectando
```

La fila contextual sustituye filas de ajustes completas; no se añade encima de todas las existentes. Objetivo orientativo: cabecera de trabajo ≤156 DIP a 1440 × 900; el área central debe priorizar el modelo. Validar a 1280 × 720, 1440 × 900 y 1920 × 1080, con escalado Windows 100/150/200 %. Usar DIP, no equivalencias rígidas a píxeles físicos.

| Zona | Contenido permanente | Contenido contextual |
|---|---|---|
| Archivo | Abrir, guardar, recientes, importar | Unidades del lote y progreso |
| Barra principal | Importar, guardar, deshacer/rehacer, herramientas de objeto | Alineación, pivote, snap, referencia |
| Preparación | Acción principal «Previsualizar» con opciones de alcance y modo | Puerto, diagnóstico y calibración en panel desplegable |
| Explorador | Modelo, nombre, visibilidad, bloqueo, cantidad seleccionada | Grupos, diseños asociados y guía activa |
| Inspector | Selección y transformaciones | Parámetros del diseño, referencia, cotas y advertencias |
| Viewport | Indicador de vista, ejes, cero y reglas | Gizmo, cotas de selección y referencia Z |
| Estado inferior | Guardado, unidades, progreso cancelable, estado real del dispositivo | «Detener proyección» visible en toda sesión física |

Quitar de la barra principal los menús vacíos de Escaneo/Modelado/Diseño que no ofrecen acciones útiles. Mantener acceso futuro mediante capacidades reales. Bluetooth/USB pertenecen al panel Dispositivo, no a pestañas de trabajo que compiten con Escena y Diseño. Conservar las consultas BLE existentes, pero no promoverlas sobre el flujo USB validado.

Paneles redimensionables, plegables y con restauración de distribución. Inspector orientativo 280–340 DIP y explorador 220–280 DIP; en ventana estrecha plegar panel secundario antes de recortar controles. Los botones secundarios pasan a desbordamiento con nombre, sin desaparecer silenciosamente. La parada no entra en desbordamiento.

## 5. Selección y manipulación 3D

### 5.1 Contrato de selección

| Gesto | Resultado |
|---|---|
| Clic izquierdo sobre pieza | Selecciona esa pieza y la hace activa |
| Ctrl+clic izquierdo | Añade/quita la pieza; conserva las demás |
| Clic en fondo | Limpia selección; Ctrl+clic en fondo no la modifica |
| Arrastre desde fondo | Selección por rectángulo; Ctrl añade/quita según operación visible |
| Ctrl+A con viewport/árbol enfocado | Selecciona piezas visibles y desbloqueadas, no texto del inspector |
| Clic derecho en pieza ya seleccionada | Conserva todo el conjunto y abre su menú |
| Clic derecho en pieza no seleccionada | Selecciona esa pieza antes del menú; Ctrl conserva y la añade |
| Escape | Cancela herramienta/gesto; fuera de gesto limpia selección |

Selección visible mediante contorno y caja/gizmo; distinguir objeto activo con marcador o grosor, no solo otro color. Mostrar «2 piezas seleccionadas» en árbol e inspector. Mantener el conjunto al cambiar de pestaña o vista. Piezas bloqueadas no se desplazan; deben poder identificarse y desbloquearse desde árbol.

Para objetos solapados: Alt+clic abre una lista breve de hits bajo el cursor, con nombre, profundidad y resaltado al recorrerla. También se puede seleccionar desde el árbol. No seleccionar a través de objetos de forma implícita; ofrecer selección a través como modo explícito. El rectángulo indicará si exige contención completa o intersección.

### 5.2 Mover, girar y escalar

- En Seleccionar/Mover, arrastrar una pieza seleccionada mueve el conjunto sobre XY, conservando Z. Umbral de arrastre del sistema evita mover con un clic involuntario.
- Gizmo con ejes X/Y/Z y planos XY/XZ/YZ, bloqueo de eje y valor desplazado junto al cursor. La flecha Z es explícita; un arrastre libre nunca cambia altura accidentalmente.
- Herramientas de giro y escala con asas grandes y preselección al pasar el cursor. Escala uniforme por defecto; no añadir escala no uniforme hasta que dominio, normales y persistencia la soporten.
- Pivote predeterminado: centro de límites del conjunto seleccionado. Opciones: objeto activo, origen de escena y orígenes individuales. Mostrar el pivote elegido.
- En selección múltiple, aplicar la misma transformación de grupo a cada pose respecto al pivote. No sobrescribir todas las posiciones con el mismo XYZ.
- Ajuste a cuadrícula opcional, paso visible y editable. Shift durante arrastre proporciona precisión fina; Ctrl se reserva para selección al iniciar el gesto, no cambia la selección a mitad de transformación.
- Capturar el puntero durante el gesto; al perder captura, desactivarse la ventana o pulsar Escape, restaurar el estado inicial de forma determinista.

**Cámara:** rueda sobre viewport = zoom hacia el cursor; botón medio = pan; botón derecho arrastrado = órbita, conservando compatibilidad actual. Menú contextual solo al soltar el derecho sin superar el umbral de arrastre. Nunca abrir menú después de orbitar. Ofrecer presets de navegación más adelante, sin exigirlos para trabajar.

Atajos propuestos: V seleccionar, G mover, R girar, S escalar, F encuadrar selección, Inicio encuadrar escena, Supr eliminar, Ctrl+D duplicar. Solo se ejecutan fuera de campos de texto. El inspector permite la misma tarea con valores y botones de incremento, como alternativa al arrastre.

## 6. Valores numéricos inmediatos

Eliminar Aplicar para pose, giro, escala y altura virtual. Un componente numérico común incluye etiqueta, unidad, valor, botones discretos de incremento y validación. Mostrar dimensiones locales y **dimensiones en escena**; diferenciar tamaño de malla, factor de escala y tamaño de guía.

| Campo | Paso normal propuesto | Shift / Ctrl | Límites |
|---|---|---|---|
| Traslación X/Y/Z | 1 mm | 0.1 / 10 mm | Límites del dominio; límite físico se valida aparte |
| Giro | 1° | 0.1 / 15° | Mantener rango actual hasta revisar normalización |
| Escala | 0.01 | 0.001 / 0.1 | Siempre positiva; límites existentes |
| Plano de referencia Z | 1 mm | 0.1 / 10 mm | Rango de escena y contexto de soporte |

Rueda modifica únicamente el campo enfocado y bajo el puntero; sin foco, desplaza el panel. Consumir el evento solo si hubo edición; no mover simultáneamente cámara, campo y scroll. Soportar ruedas de alta resolución acumulando deltas. Ofrecer preferencia para desactivar edición por rueda.

Entrada de texto: actualizar escena cuando el texto represente un número válido; permitir estados transitorios como «-» o «0,» sin convertirlos a cero ni mostrar diálogos. Aceptar cultura del sistema; serializar con cultura invariante. Rechazar NaN, infinito y rangos inválidos con mensaje junto al campo y conservar último valor válido. Enter confirma la transacción y Escape restaura el valor anterior; perder foco confirma lo válido o restaura lo inválido.

Selección múltiple: campos no coincidentes muestran «Varios». La edición de traslación del conjunto usa su pivote y desplaza todos por delta. Giro/escala se aplican según pivote común o individual, siempre con etiqueta explícita.

Respuesta objetivo: feedback visual inicial ≤100 ms y manipulación ≥30 FPS en una escena de referencia acordada. Son presupuestos de diseño, no rendimiento medido. Renderizar poses durante arrastre sin reconstruir mallas ni índices; recalcular guías caras en worker con cancelación y descarte por revisión. Al escribir, agrupar ráfagas de aproximadamente 400 ms; el estado mostrará «Actualizando guía» y evitará presentar una guía antigua como actual.

## 7. Deshacer y rehacer

Ctrl+Z / Ctrl+Y, también Ctrl+Shift+Z para rehacer. Botones con tooltip «Deshacer: mover 2 piezas». En edición de texto, el campo recibe primero sus acciones de texto; el historial de escena actúa al terminar la transacción.

Una operación por gesto: arrastrar 200 eventos = un deshacer; girar la rueda en una ráfaga = uno; importar cinco archivos = un lote; borrar una selección = uno. Incluye importar, transformar, duplicar, eliminar, agrupar, visibilidad/bloqueo, referencias y diseños. Cámara y selección no contaminan el historial principal; restaurar una operación puede recuperar su selección asociada.

Guardar checkpoint para cambios pendientes; deshacer hasta él elimina el indicador de suciedad. Una edición nueva tras deshacer invalida la rama de rehacer. Guardar no vacía el historial durante la sesión. Compartir mallas inmutables entre estados y establecer presupuesto de memoria, sin clonar buffers GPU por operación. No exigir historial persistente entre reinicios en la primera entrega.

**Hardware:** conectar, iniciar, detener o cargar al LP2 no son acciones reversibles de escena. Deshacer nunca vuelve a iniciar proyección ni grabado. Durante emisión, deshacer una pose debe detener y confirmar reposo antes de cambiar el estado; el siguiente inicio físico exige acción del operador. Ofrecer aviso no modal «Escena restaurada; proyección detenida».

## 8. Varios STL y guías de conjunto

Importar varios archivos con un único diálogo, o arrastrarlos al viewport/explorador. Preguntar unidades una vez por lote, con opción de corregir cada archivo. Conservar origen y pose por defecto para ensamblajes; ofrecer «Distribuir para inspección» como acción voluntaria. No separar ni centrar piezas automáticamente, porque destruiría su registro relativo.

Validar archivos por separado, mostrar progreso y resumen de fallos; permitir conservar los válidos y deshacer todo el lote aceptado. Cancelar restaura el estado previo del lote. No perder el proyecto por un STL incorrecto. Mantener límites actuales de modelos/triángulos y explicar cuál se excede.

### 8.1 Tres decisiones independientes

| Decisión | Opciones | Valor inicial |
|---|---|---|
| Alcance | Selección / todas las piezas habilitadas / grupo | Selección; sin selección, pedir alcance en vez de adivinar |
| Geometría | Silueta del conjunto / contornos por pieza / rectángulo conjunto / sección a Z | Silueta del conjunto |
| Referencia de visualización | Superior / inferior / cara elegida / soporte / Z personalizado | Superior del alcance, etiquetada como referencia visual |

**Silueta del conjunto:** transformar todos los triángulos de todas las piezas del alcance y unir sus proyecciones XY. Conservar salientes y perímetros separados; omitir agujeros interiores por defecto para colocación. Los bordes internos entre piezas solapadas desaparecen. Una pieza baja que sobresale de otra alta debe contribuir al contorno.

**Contornos por pieza:** calcular cada silueta de forma independiente. Mostrar color/etiqueta por pieza y mantener sus bordes aunque se superpongan. Exportación y vista previa deben conservar esos recorridos; advertir que zonas coincidentes pueden recorrerse más de una vez. Útil para colocar varias piezas físicas separadas.

**Rectángulo conjunto:** calcular extremos sobre toda la selección transformada; una sola caja exterior. Ofrecer rectángulos individuales como opción secundaria cuando el objetivo sea posicionar piezas por separado.

**Sección a Z:** intersección triángulo–plano, unión de segmentos y construcción de lazos; no equivale a proyectar triángulos. Plano tangente, caras coplanares, extremos compartidos y mallas abiertas requieren tolerancia documentada y diagnóstico. No cerrar huecos inventando geometría. Por defecto omitir agujeros de la guía exterior, con opción futura de mostrarlos como referencia sin emisión.

La guía se regenera automáticamente después de una selección/pose válida; sustituir «Generar guía» como paso obligatorio por un indicador y «Actualizar» en opciones avanzadas. «Previsualizar selección» prepara la revisión actual y usa el modo elegido en una acción, sin abrir otro asistente. Si el resultado no está listo, espera esa revisión cancelablemente; nunca inicia una guía de revisión anterior.

### 8.2 La captura de las piezas cruzadas

Prueba de referencia obligatoria: cargar riel y seguro, conservar poses distintas, Ctrl+clic en ambos. La silueta conjunta debe abarcar la unión de los dos desde arriba, incluyendo cualquier parte baja que sobresalga. En contornos individuales se ven ambos bordes completos, aun donde se cruzan. Un rectángulo abarca ambos. Una sección Z muestra solo lo que intersecta ese plano; puede excluir legítimamente una pieza situada por encima o debajo. La UI debe explicar esa diferencia con una miniatura y nombres precisos, no esconderla bajo «cara más alta».

## 9. Alturas, soporte y caras inferiores

«Plano mínimo de la impresora» se interpreta provisionalmente como plano de apoyo de escena/fixtura, inicialmente Z=0. Studio controla un grabador, no una impresora; nombrarlo **Plano de apoyo** y permitir calibrar su Z. Confirmar este término durante validación de diseño, sin bloquear el resto del plan.

- Superior/inferior usan límites Z transformados del alcance para referencias horizontales. No usar el centroide de un triángulo como sinónimo de altura máxima/mínima.
- Cara elegida conserva pieza, punto y normal en coordenadas de objeto; seguir pose es explícito. Una cara inclinada debe verse como plano inclinado, no convertirse silenciosamente a horizontal.
- «Apoyar sobre plano» transforma el conjunto o cada pieza hasta su mínimo Z; la selección del comportamiento se muestra en el comando.
- Plano de corte con slider, entrada numérica y arrastre de asa Z; mostrar cota y sección en color diferenciable. Si el corte coincide con cara coplanar, usar contorno de la superficie coplanar; si solo toca una arista/punto y no hay área cerrada, mostrar «Sin sección cerrada».
- «Cara inferior» no significa que el láser pueda atravesar la pieza. Mostrar oclusión y necesidad de reorientar físicamente; nunca invertir la pieza automáticamente para alcanzar esa cara.

Separar tres conceptos: altura donde se dibuja la guía, superficie objetivo del diseño y distancia de enfoque del láser. Un recorrido LP2 XY no controla Z ni compensa enfoque dinámico. Para alturas distintas, ofrecer revisión por pieza/altura y advertir limitaciones de enfoque; no prometer grabado uniforme sobre toda la unión.

El cono conserva su parte original hasta referencia y naranja tenue después. La leyenda explica que representa geometría estimada; el campo físico validado por USB no crece al aumentar la altura virtual. En el perfil actual, mantener el límite verificado de 100 × 100 mm; no sustituirlo por 110 × 110 mm por apariencia o documentación de otro perfil.

## 10. Menú contextual

Máximo aproximado de ocho entradas principales, agrupadas. Mostrar nombre/cantidad en encabezado. Comandos no disponibles indican motivo en tooltip o panel de ayuda; evitar listas largas de funciones futuras deshabilitadas.

| Contexto | Acciones principales | Submenús / opciones |
|---|---|---|
| Una pieza | Mover, girar, escalar; duplicar; encuadrar; previsualizar; eliminar | Alinear: centrar XY, apoyar sobre plano, restablecer pose. Superficie: elegir cara, superior, inferior, corte Z. Visibilidad/bloqueo. Renombrar |
| Varias piezas | Transformar selección; duplicar; agrupar; alinear; previsualizar selección; eliminar selección | Distribuir con separación; centro/pivote; silueta conjunta o contornos por pieza; ocultar/bloquear selección |
| Grupo | Transformar, duplicar, entrar al grupo, desagrupar | Guía de grupo, pivote y visibilidad |
| Imagen/diseño | Mover sobre superficie, girar, escalar, ajustar a área | Reasignar superficie, repetir, ocultar del trabajo, quitar diseño |
| Fondo | Importar, seleccionar todo, encuadrar escena | Vista superior, mostrar reglas, cuadrícula, ajuste, plano de apoyo |

Separar «Quitar de escena» de cualquier borrado de archivo en disco. Duplicar genera nuevos IDs y comparte geometría inmutable; aplicar pequeño desplazamiento visible, reversible. Agrupar no es hacer unión booleana de sólidos. No incorporar booleanas 3D, reparación automática de mallas ni edición de vértices en esta fase de usabilidad.

## 11. Diseños e imágenes sobre superficies

Modelo mental: **Pieza → superficie objetivo → diseño asociado → trabajo**. La imagen es un objeto editable del proyecto, no una textura pegada sin relación persistente.

Arrastrar PNG/JPEG sobre una pieza muestra una previsualización y resalta la superficie destino antes de soltar. No emitir ni iniciar trabajo al soltar. Clic elige diseño; Alt+clic permite elegir la pieza inferior; árbol ofrece selección inequívoca. Proporcionar «Añadir imagen a superficie» como alternativa sin arrastre.

Primera entrega funcional posterior: proyección planar, tamaño físico, opacidad, orientación, relación de aspecto bloqueada y gizmo sobre el plano. Tiradores desplazan el diseño en coordenadas de superficie; el movimiento de la pieza arrastra su diseño y conserva la relación. Mostrar caras ocultas, recortes, cobertura fuera de malla y diseño fuera del campo útil. Separar zoom de imagen de tamaño físico.

Guardar recursos embebidos, IDs de asociación, transformación y método de proyección en el proyecto versionado; undo/redo para todas las operaciones. Si una malla se reemplaza y pierde la referencia, marcar diseño sin superficie y pedir reasignación, sin proyectarlo sobre otra cara por proximidad silenciosa.

Etapas siguientes: máscara/área grabable, proyección cilíndrica, UV, raster y compensación; validar capacidades ópticas y mecánicas antes de activarlas. La presente propuesta organiza el flujo y sus contratos, no declara implementado el grabado de imágenes 3D.

## 12. Reglas, retícula y medición

En vista superior ortográfica, reglas X/Y fijas a los bordes del viewport y sincronizadas con mundo, zoom y pan. Cero central óptico identificado como «Origen láser», además del origen de escena si difieren por calibración. Valores negativos a izquierda/abajo; unidades mm permanentes.

Retícula adaptativa con pasos 1/2/5 × 10ⁿ mm; elegir densidad para que etiquetas mayores se separen aproximadamente 60–100 DIP. Marcas menores sin texto, mayores con texto; evitar solapamiento de números, ejes y selección. El ajuste usa su propio paso indicado, no cambia de precisión al cambiar zoom.

En perspectiva, no presentar la regla de pantalla como longitud física uniforme: usar retícula sobre plano activo con cotas proyectadas, indicador «Perspectiva» y acceso de un clic a vista superior para medir. Cotas de ancho/alto de selección y de distancias a origen visibles sin ir al inspector. Herramienta Medir entre puntos con proyección XY y distancia 3D diferenciadas.

Mostrar campo LP2 con línea discontinua y rótulo de límite, separado de retícula. Sección, selección, guía, trayectoria y zona fuera de campo deben tener patrones/grosores distintos además de color. Mantener malla, etiquetas y marcas legibles al alejar la cámara; ocultar detalles secundarios antes que el cero y las cotas principales.

## 13. Sistema visual oscuro inspirado en LaserPecker

### 13.1 Procedencia y decisión

La web oficial consultada contiene acentos `#FDD800`, `#FED431` y `#FBCD0A`, además de negro/blanco. Se eligió `#FDD800` como referencia de amarillo para Studio. Es una adaptación de los colores publicados, **no un manual oficial de marca ni un valor Pantone certificado**. El verde y morado actuales dejan de competir como acentos primarios; se reservan para estados y overlays específicos.

La primera búsqueda de `ui-ux-pro-max` devolvió HUD de ciencia ficción y una estructura de marketing; se descartó por no ajustarse a CAD/CAM. El segundo resultado aporta estilo plano y feedback discreto, pero su estructura de landing y paleta clara también se descartan. No hubo una coincidencia verificada de sistema completo para CAD/WPF: la composición siguiente es una síntesis propia. La búsqueda WPF de entradas numéricas no tuvo resultados; una búsqueda más amplia sí respaldó comandos, parámetros e InputBindings. La interacción numérica específica es propuesta de este documento.

### 13.2 Tokens propuestos

| Token semántico | Color | Uso |
|---|---|---|
| Background / Viewport | `#101216` | Fondo principal neutro |
| Surface | `#1B1E24` | Barra y paneles |
| SurfaceRaised | `#242933` | Campos, menús y panel flotante |
| Divider | `#343B47` | Separación decorativa; no único límite funcional |
| ControlBorder | `#667181` | Bordes necesarios para identificar campos |
| TextPrimary | `#E7EAF0` | Texto principal |
| TextSecondary | `#AAB3C0` | Etiquetas y ayuda legible |
| Accent / SelectionOutline | `#FDD800` | Acción primaria y borde de selección |
| OnAccent | `#111318` | Texto/icono sobre botón amarillo |
| AccentHover | `#FFE45C` | Hover de acción principal |
| FocusRing | `#79B8FF` | Foco de teclado, distinto de selección |
| Success | `#57D69B` | Reposo confirmado y operaciones correctas |
| Warning / BeyondReference | `#FFAF45` | Advertencia y tramo fuera de referencia |
| Danger | `#FF727C` | Parada y estado desconocido, con texto/icono |
| MeshDefault | `#AAB7CA` | Malla sin seleccionar |
| MeshSelected | `#C9CFD9` | Malla seleccionada, con contorno amarillo |
| Guide | `#79B8FF` | Guía calculada; línea continua |
| Section | `#D6A8FF` | Corte a Z; línea de puntos |

No aplicar amarillo de fondo a todos los botones. Una acción primaria por contexto. «Detener» conserva prioridad y etiqueta visible aunque no sea la acción primaria. Rojo no es sinónimo de «Eliminar de escena» en todo momento; la parada física merece mayor jerarquía.

Contraste sRGB calculado para los tokens sólidos: texto principal/surface 13.85:1; secundario/surface 7.89:1; OnAccent/Accent 13.27:1; Accent/Background 13.39:1; FocusRing/Surface 8.04:1; ControlBorder/Surface 3.37:1. Son pares propuestos, no una auditoría de la aplicación completa. Verificar composición final, opacidades, hover y foco. Divider no cumple rol de borde accesible por sí solo.

Malla neutra y selección por contorno evitan cubrir la imagen futura con verde intenso. Cono original amarillo al 3–5 % orientativo, tramo naranja al 5–8 %; ajustar sobre el fondo real y no depender de esas transparencias para comunicar límites. Mantener X/Y/Z con convención rojo/verde/azul, texto y letras, independientemente del amarillo de marca.

### 13.3 Tipografía, densidad y estados

Segoe UI Variable/Segoe UI del sistema: texto 13–14 DIP, títulos de panel 14–16 DIP, etiquetas técnicas ≥12 DIP. Números tabulares y precisión visual limitada a 2–3 decimales; valor completo al editar, sin perder precisión almacenada. No usar mayúsculas en todos los títulos ni fuentes futuristas para información operativa.

Escala de espacios 4/8/12/16/24 DIP; controles estándar 32 DIP de alto, botones iconográficos con área ≥32 × 32 DIP, modo táctil opcional ≥44 × 44 DIP. Esta adaptación es para escritorio; no trasladar sin criterio las medidas móviles de la habilidad. Iconos coherentes con WPF UI, normalmente 16–20 DIP, tooltip y nombre accesible. Radios 4–6 DIP; sombras suaves solo en superficies flotantes; sin neón, parpadeos decorativos ni fondos translúcidos sobre texto.

Hover, presionado, seleccionado, foco y deshabilitado son estados diferentes. Animación discreta de 100–180 ms cuando ayude a localizar cambios; respetar reducción de movimiento y alto contraste de Windows. Narrador debe anunciar selección múltiple, campo/unidad/error y cambios de sesión sin narrar cada frame. Tabulación coherente y alternativa de teclado para todas las operaciones principales.

## 14. Estados de guía y proyección física

| Estado | Mensaje y conducta |
|---|---|
| Sin selección | «Selecciona piezas o elige Toda la escena»; no transmitir |
| Calculando | «Actualizando guía de 2 piezas»; mostrar revisión pendiente |
| Lista | «Guía de 2 piezas · 72 × 48 mm · plano Z 0»; iniciar habilitado |
| Fuera de campo / sección abierta | Problema junto a la guía y resaltado de zona; inicio bloqueado |
| Preparando USB | Puerto y progreso; cancelar disponible |
| Proyectando | Modalidad, alcance, nivel, tiempo restante y parada permanente |
| Actualizando físicamente | «Deteniendo para actualizar»; no afirmar que ya está mostrando el nuevo contorno |
| Reposo confirmado | Confirmación breve; mantener guía editada visible |
| Desconexión sin parada | «Estado del láser desconocido»; bloquear inicio y mostrar instrucción de parada física |

La edición en vivo del viewport es predeterminada; el seguimiento físico es una decisión independiente. Propuesta inicial: durante una sesión explícita, actualizar al soltar un arrastre o terminar una ráfaga numérica, con modo «Seguir cambios» visible. No cargar un archivo por cada pixel. Mantener Stop → reposo → carga → inicio y coalescer peticiones. Si cambian dispositivo, calibración o alcance de forma incompatible, detener y pedir nuevo inicio.

Conservar el nivel probado y límite temporal actuales hasta otra validación. El ID del archivo debe seguir dependiendo del contenido, incluida pose y selección; el fallo previo de recuperar una figura antigua es una regresión crítica. No borrar archivos del LP2 automáticamente para ocultar el crecimiento de caché.

## 15. Plan de implementación por entregas

| Entrega | Prioridad | Trabajo y dependencias | Condición de salida |
|---|---|---|---|
| E1 — edición confiable | P0 | Selección múltiple, alcance visible y transacciones Undo/Redo. Base compartida antes de gestos complejos | Dos piezas se seleccionan/transforman/restauran juntas sin perder pose relativa |
| E2 — manipulación rápida | P0 | Arrastre/gizmos, campos inmediatos/rueda, pivote y snap; depende de E1 | Mover con cursor y volver exactamente al estado previo con un deshacer |
| E3 — guías útiles | P0 | Colección de mallas, unión/per-pieza/rectángulo, referencias separadas, sección Z; depende de E1 | Caso de piezas cruzadas y corte inferior pasan pruebas geométricas y visuales |
| E4 — espacio de trabajo | P1 | Barra compacta, menú contextual, dispositivo unificado, reglas y tokens oscuros | Flujos frecuentes sin cambios de panel innecesarios ni textos recortados |
| E5 — composición de diseños | P1 | Imagen asociada a pieza/superficie, proyección planar, recursos de proyecto y undo; depende de E1–E3 | Mover pieza conserva diseño y guardar/reabrir restaura asociación |
| E6 — preparación avanzada | P2 | Medición, distribución/repetición, calibración y superficies cilíndricas/UV | Validación propia por capacidad, sin presentar simulación como prueba física |

La importación por lote puede desarrollarse en E1; tokens y reglas pueden diseñarse mientras se estabiliza selección. No invertir el orden haciendo únicamente E4: mantendría los principales problemas de uso.

### Puntos de integración para el plan técnico

- Sustituir `Selected` como única fuente por IDs seleccionados + ID activo; conservar adaptador para inspector individual durante migración.
- Introducir servicio de herramientas de viewport y captura de gestos; mantener geometría y sesiones de hardware fuera de los handlers de ratón.
- La pose actual solo conserva giro Z. Antes de ofrecer giro libre X/Y/Z, ampliar el contrato a orientación 3D (por ejemplo, cuaternión), documentar orden de composición y migrar proyectos con el giro Z equivalente. Mantener edición Z inmediata como primera entrega; no dibujar asas X/Y que no tengan efecto. Recalcular normales, límites y referencias en espacio transformado; evitar obtener alturas usando solo Z local multiplicada por escala.
- Historial transaccional de operaciones sobre estado de proyecto; comandos MVVM compartidos con `CommandParameter` e `InputBindings`.
- Contrato de guía con alcance, revisión, poses, geometría, referencia y diagnóstico; cálculo independiente de Helix. El resultado renderizable es un adaptador.
- Componente numérico WPF con validación, cultura, rueda y transacción; no dispersar handlers distintos para cada TextBox.
- Estado de sesión/dispositivo centralizado, sin mensajes BLE que contradigan USB activo.
- Recursos semánticos `ResourceDictionary` con tokens; reutilizar WPF UI sin añadir un framework de docking antes de demostrar necesidad. Evaluar licencia/compatibilidad si se incorpora uno.
- Versionar proyecto al añadir grupos, referencias y diseños. Mantener guardado atómico y recursos embebidos; pruebas de apertura de proyectos existentes.

## 16. Criterios de aceptación y verificación

Todos son requisitos futuros, no casillas ya aprobadas.

| ID | Comprobación observable | Evidencia requerida |
|---|---|---|
| UX-01 | Ctrl+clic selecciona/quita dos piezas en árbol y viewport; objeto activo distinguible | Prueba UI y estado de selección |
| UX-02 | Arrastre XY conserva Z y separación entre piezas; Escape restaura; órbita no abre menú | Prueba de gestos en Windows |
| UX-03 | Deshacer/rehacer importación, borrado y gesto múltiple restaura poses/IDs/diseños; un gesto = una entrada | Tests del historial y recorrido UI |
| UX-04 | Cinco STL en un lote conservan registro relativo, unidades y errores individuales; cancelación sin cambios parciales | Tests de importación y diálogo/drag-drop |
| UX-05 | Rueda sobre campo enfocado modifica solo ese campo; texto inválido no transforma; no existe Aplicar de pose | Prueba con mouse y teclado, culturas ES/EN |
| UX-06 | Selección de dos piezas cruzadas genera unión con salientes de ambas; modo individual conserva ambos contornos | Fixtures geométricos y capturas de resultados |
| UX-07 | Rectángulo conjunto contiene todos los puntos del alcance transformado | Tests de rotación, traslación, escala y piezas separadas |
| UX-08 | Superior/inferior/soporte/cara/corte Z producen la referencia indicada; corte abierto se diagnostica | Tests de planos tangentes, coplanares, mallas abiertas y varias alturas |
| UX-09 | Visor y G-code tienen mismos perímetros, coordenadas, unidades y selección | Comparación numérica del resultado, no solo captura visual |
| UX-10 | Cambiar de figura/pose/selección cambia contenido e identidad cuando cambia G-code | Transporte simulado con caché por ID y prueba física separada |
| UX-11 | No hay transmisiones fuera de sesión explícita; undo no reactiva emisión; stop y desconexión coherentes | Tests de máquina de estados y transporte, luego hardware supervisado |
| UX-12 | Reglas muestran negativos, cero y mm; zoom/pan no cambian snap ni fingen escala uniforme en perspectiva | Pruebas visuales en vistas y DPI distintos |
| UX-13 | Menú contextual conserva selección múltiple; comandos muestran alcance correcto | Prueba contextual con una, varias y ninguna pieza |
| UX-14 | Soltar imagen la asocia a superficie sin emitir; mover pieza y reabrir proyecto preserva diseño | Pruebas de asociaciones/persistencia y revisión visual |
| UX-15 | Contraste de texto ≥4.5:1 y controles informativos ≥3:1; foco, alto contraste y Narrador utilizables | Medición final de colores compuestos y recorrido accesible |
| UX-16 | Interfaz no recorta inspector/estado/parada a 1280 × 720 y DPI 150/200 % | Capturas Windows; registro de escalado y resolución |
| UX-17 | Cambios rápidos descartan cálculos obsoletos; UI mantiene respuesta en escena de referencia | Medición p95 de respuesta, frame time y cancelación |

Usabilidad: ejecutar con tres usuarios de nivel distinto los flujos importar/seleccionar dos piezas, alinearlas, deshacer un error, elegir sección inferior y previsualizar alcance. Registrar tiempo, número de clics, errores y ayudas requeridas. Objetivos iniciales: selección doble sin ayuda; mover sin inspector; guía seleccionada en una acción; ninguna interpretación errónea de «simulación» frente a «proyección física». Comparar contra la versión base antes de fijar metas porcentuales.

En cada entrega de código: build y tests Release, fixtures geométricos, validación visual Windows y persistencia según el área tocada. Pruebas automatizadas sin emisión física. Separar aceptación de software, aceptación visual y validación física del LP2.

## 17. Decisiones y riesgos pendientes

1. Confirmar si «plano mínimo» es apoyo Z=0, plano calibrado de fixtura o corte variable; se ofrecen los tres sin confundirlos.
2. Validar con usuarios el arrastre directo XY frente a activación de herramienta; propuesta inicial prioriza un gesto y hace visible la herramienta.
3. La sección de mallas no manifold puede carecer de lazos cerrados: mostrar diagnóstico, no prometer reparación.
4. Diseño sobre superficie inferior/oculta requiere reorientación y calibración; no se habilita emisión por verla en pantalla.
5. Uso del amarillo en guía y selección puede confundirse: la propuesta separa selección amarilla, guía azul y sección morada con estilos de línea.
6. El LP2 conserva archivos por ID; seguimiento físico intensivo puede aumentar almacenamiento. Diseñar gestión posterior con autorización, sin eliminar datos durante edición.
7. El agrupado, los diseños y la sección aumentan el estado persistente: planificar migración y rollback del formato antes de implementarlos.
8. Una selección enorme puede generar más recorrido del admitido: mostrar coste estimado y límite antes de iniciar, conservando la edición disponible.

## 18. Fuentes y método

Fuentes consultadas el 13 de noviembre de 2026. Son referencias de patrones, no autorización para copiar código propietario ni prueba de capacidades físicas de LP2.

- [LaserPecker: sitio oficial](https://www.laserpecker.net/). HTML/CSS publicados: amarillo `#FDD800`, variantes `#FED431`/`#FBCD0A`, negro y blanco. La selección de tokens oscuros es propia.
- [LDS Edit Bar](https://support.laserpecker.net/hc/en-us/articles/9556856334095-LDS-Edit-Bar). Referencia de agrupación, alineación, tamaño y edición reversible; no implica booleanas 3D en Studio.
- [LDS App Operation Guide](https://support.laserpecker.net/hc/en-us/articles/15412901098255-LDS-App-Operation-Guide). Referencia de flujo móvil y previsualización; usa LP4 de ejemplo y advierte variaciones por dispositivo. No extrapolar sus funciones de hardware a LP2.
- [Microsoft: personalizar menús y barras](https://learn.microsoft.com/en-us/visualstudio/ide/how-to-customize-menus-and-toolbars-in-visual-studio) y [distribución de ventanas](https://learn.microsoft.com/en-us/visualstudio/ide/customizing-window-layouts-in-visual-studio). Inspiración para comandos estables y paneles recuperables.
- [Blender: precisión de transformaciones](https://docs.blender.org/manual/en/latest/scene_layout/object/editing/transform/control/precision.html). Referencia de precisión y restricciones; atajos de Studio se definen aquí para evitar conflictos.
- [W3C: alternativas a arrastrar](https://www.w3.org/WAI/WCAG22/Understanding/dragging-movements). Referencia accesible; aplicar a escritorio como guía, no declarar conformidad web de WPF.
- [Microsoft: comandos WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/commanding-overview). Base de comandos compartidos, parámetros y atajos.
- Habilidad local `ui-ux-pro-max`: búsquedas de sistema visual, WPF commands y dragging movements; resultados filtrados según adecuación al escritorio. Reglas de contraste, foco y reducción de movimiento adaptadas a Windows.
- Repositorio: `MainWindow.xaml`, `MainWindow.xaml.cs`, `MainWindow.Placement.cs`, `MainWindow.SurfaceReference.cs`, `MainWindow.UsbPreview.cs`, `StudioViewModel.cs`, `PlacementGuide.cs`, `UsbPreviewJob.cs` y `UsbPreviewSession.cs`.
- Captura aportada: `image-1.png` del mensaje de objetivo. Se inspeccionó visualmente; no se incorpora una ruta absoluta al proyecto ni se atribuyen a la captura comportamientos que solo puede demostrar una prueba interactiva.

### Trazabilidad de la solicitud

Cursor y movimiento: §5. Multiselección Ctrl+clic: §5.1. Deshacer/rehacer: §7. Varios STL y guía conjunta: §8. Rueda numérica y retirada de Aplicar: §6. Cara inferior/plano mínimo: §9. Menú contextual individual/múltiple: §10. Piezas cruzadas a distinta altura: §8.2. Imágenes sobre figuras: §11. Barra tipo Visual Studio: §4. Reglas/cuadrícula: §12. Apariencia moderna y reducción de clics: §§3–4 y 13. Paleta LaserPecker oscura: §13. Entregable para plan: entregas, dependencias y aceptación en §§15–17.
