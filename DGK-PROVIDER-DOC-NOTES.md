# Условия для квалификации production DGK provider

## Дополнение: официальный C# wrapper

Закреплённый [PSModel.cs](https://github.com/Autodesk/PowerShapeAndPowerMillAPI/blob/f0761e50ae833e7647423b8da3ba24bfc23bd93d/Delcam.ProductInterface.PowerShape/Entities/Model/PSModel.cs)
получен 14 октября. Локальный reference snapshot:
`work/K8-autodesk-api-source/PSModel.cs`, SHA256
`46fc3bf6e88c22d67b4891ec3d8f71162db6e0f3f7e2dbcf8843a7fd80fba4f1`.
Он не запускается и не копируется в production.

- SelectedItems, строки 541–619: wrapper возвращает пустой список при null
  GetSelectedItems и игнорирует отдельные exceptions при entity mapping.
  Поэтому его список не является строгим доказательством полного выделения.
  Actual PowerShape2023 empty case дал null; native strict refusal сохранён.
- Export, строки 1569–1646: есть явные branches для Selected и World, wizard
  TARGET UNKNOWN и дополнительный NEXT при drawing count !=0. Это источник
  command recipe, но не getter эффективных Manufacturing/export options.
- Wrapper меняет FORMUPDATE и завершает export цепочкой YES/YES/DISMISS.
  Нельзя переносить эти скрытые UI changes и неизвестные confirmations в
  general provider. Wizard/frame recipe требует отдельной квалификации2023;
  комментарии wrapper также содержат TODO для workplane branch.
- PSEntity.AddToSelection, строки 197–213: может активировать слой, прежде
  чем добавить объект. Provider должен требовать уже активный слой и проверять
  explicit selection prefix; не вызывать этот helper с побочными действиями.

Следующий конкретный read-only сценарий:
`K8-NONEMPTY-SELECTION-READ-SCENARIO.md`. Он не запускает SDK wrapper,
не открывает вторую PowerShape instance и не экспортирует.

14 октября 2026,Autodesk Product Help en_US/PWRSS2023. Эти сведения уточняют
будущий provider;они не доказывают getter действующих настроек/command syntax.

- [Exporting data](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-438C8FF1-50A9-4E0C-812B-0D628C04F610): DGK default World;wizard может изменить frame.
  Пустое selection может расширить export. Требуется exact nonempty owned set.
- [Using Export wizard](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-9665340F-B4B4-4052-A7CF-EC470B0D220C): выбранный target system/settings,
  drawing/workplane pages меняют сценарий. Badly trimmed surfaces могут вызвать
  выбор untrimmed export или abandon. Нельзя автоматически согласиться на
  изменённую геометрию или нажать Yes по неизвестному prompt.
- [Export options](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-0835D1CB-F775-46EE-8E4F-F21C7C630667): target PowerMill feature version
  определяет XML/MAC,hole feature export/geometry/chamfer/tolerance влияют на
  данные;assembly multi-file option может создавать дополнительные файлы.

Provider должен закрепить/проверить эффективный frame/variant/translator/feature
policy/assembly file scope и qualified native selection layout. Нет invented
getter,подмены global tolerance экспортным tolerance или автоматического retry.
Геометрию результата принимать независимым owned import/roundtrip,а не по SHA
или mesh witness. До такого подтверждения квалификации остаются false.
