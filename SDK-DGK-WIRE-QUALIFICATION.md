# Сквозная подготовка DGK: SDK → native filesystem recipe

`verify-owned-dgk-wire.ps1` создаёт отдельный marker-protected stage. C# SDK host
работает через настоящий STDIO. Изолированный panel helper забирает requests и
вызывает настоящий Framework worker. Working-copy/inspection/binding/set steps
используют явно fake CAD context; последний prepare_owned_dgk_export проходит
через **QueueOperations.Run**, то есть production filesystem dispatch.
В этом ответвлении return происходит до COM branch. CAD не подключается.

Каждый сценарий выполняет ровно пять queue requests:prepare_working_copy,
inspect_active_model,bind_working_document,prepare_entity_set,prepare_owned_dgk_export.
Client-private handles не проходят из другой связи; лишний command аргумент
отвергается до submission. В read-only каталоге DGK prepare отсутствует.

Шесть случаев:control/source_tamper/fenced_copy/lost_reply/target_tamper/
directory_replaced. Control выдаёт ownedDgkTargetHandle с bounded TTL. При
ошибке/потерянном ответе нет handle или retry; original request ID сохраняется.
Target preparation не пишет mutation journal/once claim/export result и не
разрешает fence. Raw target bytes/reservation/artifact directory/ownership
и source/working/checkpoint identity проверяются независимо.

Два exact runner прохода дали6 cases/30 requests/219 audited hashes;commandCalls0.
Первый stage work/K9-dgk-wire,final exact runner work/K9-dgk-wire-runner.
Маркерный отказ проверен до создания fixture work. Native fixture markers нельзя
добавлять в deploy package. UI/window/model context в этих tests — fake;
это не live PowerShape qualification и не исполнение DGK export.

Далее настоящие COM getter/selection/settings provider и SDK execute/verify
boundaries,согласованный live own-copy DGK export/import/geometry roundtrip.
Существующие generic transforms/organization/sections и вся матрица К10/К11
сохраняются. Tests не заменяют native provider/geometry acceptance.
