# К8 — неизменяемые измерения в журнале поворота

Внутренний rotation execution plan теперьschema2. Для каждого target до первой
команды записывается отдельный rotationmeasurements/<execution>/<index>.before.json.
Plan закрепляет SHA. После успешного return иcontext checks появляется after.json;
summary связывает before/after SHA сstarted/returned/observed records.
Исходный schema1 без измерений не выдаётся за новый контракт.

Record schema1 закрепляет execution ID,index,phase,recordedUtc, copy/owner/working
SHA,binding/inspection IDs/SHA,source observation/window digest иexpiry. recordedUtc
обозначает запись на диск, ане атомарное время всех точек. Payload содержит native
key,world/document_raw,descriptor SHA,unique anchors,positions,directions,invariants
иpaired bounds. Exact-field/type/finite/shape checks не принимают bool вместочисла,
неоднозначные anchors иличужие provenance. FileMode.CreateNew не перезаписывает proof.

Перед target callback before запись перечитывается по pinned SHA; fresh measured
samples должны совпасть. После этого снова проверяется весь текущий контекст:
долгое чтение измерений не даёт права исполнить команду после expiry/сменыдокумента.
Proof streams держатся до завершения. Попытка изменить/удалить held record запрещена.
Возможные частичные before artifacts обозначаются localArtifactsMayExist.

## Проверки

- 24 native batch cases/577 independently audited hashes прошли в
  evidence/K8-rotation-receipts/execution-faults,
  stage work/K8-rotation-receipts-faults,session55131 завершён успешно.
  Дополнительные cases: malformed before samples →0 commands; drift приперечитывании
  before →unknown/0 calls сclaim/fence; malformed after →unknown послеоднойкоманды.
- 16 immutable record cases/256 hashes прошли в
  evidence/K8-rotation-receipts/records-qualified,
  stage work/K8-rotation-records-audited,session9043 завершёнуспешно.
  Header/schema/phase/index/provenance/SHA/vector/type/extra-fields отказы,
  foreign execution,rewrite иheld tamper не изменили существующие proofs.
- 21 .NET mathematical predicate tests прошли безskip в
  evidence/K8-rotation-receipts/kernel-regression,session26301 завершёнуспешно.

Record CLI безмаркерa отказывается иничего не создаёт. Новый record runner
включён вverify-sdk.ps1; batch auditor теперьпроверяет measurement linkage.
Предыдущий PR31/0d11c5e108c044188305f94e07a900c1bd094c34 прошёл четыре CI:
38472860435/38472958341 baseline,38472860388/38472958423 SDK;
его native531 regression тожеpassed. Новыйsource полныйCI ещётребуется.

## Предел доказательства

Batch fake теперьвозвращает восемьуглов своего искусственного bbox как witnesses.
Это исключительно механизм испытания записи/связей/защит; точкиbbox не выдаются
за геометрию реального тела. Синтетический effect не является физическим поворотом.
Даже noop success остаётся executed_unverified иcopy fenced.

Нет независимого native verifier/fence release илиproduction rotation queue/COM/
MCP executor. Реальный measurement provider ещёне имеетsolid face samples илиlive
layout qualification. Передвыпуском нужнысертификаты источникаmeasurements,
fresh independent after measurements,calibration sign/frame иотдельныйfault
recovery длячастичногопакета. Record alone не даётexecution/verification authority.

К7 live approval всёещёpending; installed runtime/READY packages/реальныйCAD
этимблокомне менялись. ПолныйК8 иК9–К11 незавершены. Следом независимыйverifier
по pinned before-records,провайдер тела,production transport/faults иlive scenario.
