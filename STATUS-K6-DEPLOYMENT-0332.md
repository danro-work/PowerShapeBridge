# Bridge0.33.2-dev — установка подтверждена

13 октября 2026 пользователь разрешил обновление0.33.0-dev →0.33.2-dev
с резервной копией и перезапуском панели. Применён ранее квалифицированный
неизменный пакет work/K6-native-directory-live, build-manifest SHA
9eb1fa4f9ee4cbe84ccd123c264ba5249cd91c5c3b0d59be0838910483594562.

Backup: work/general-deployment-backup-20261013-070902. Все18 installed hashes
совпали с планом, все18 backup hashes — с предыдущим runtime. Одна новая
панель PID23584, version0.33.2-dev/general-v1; свежий heartbeat/config SHA,
idle queue/no worker проверены. Deployment script завершился успешно.

Перед установкой исправлено чтение heartbeat: shared reader вместо Get-Content;
при sharing violation32/33 только чтение считается временно недоступным и
повторяется существующим readiness loop. Restart/copy/CAD не повторяются.
Six isolated report-reader checks passed; malformed JSON не маскируется.

Свежие CAD reads подтвердили прежние пять окон, process/start/session,
активную own Working_b3d16256be1a4848bdbf15f6160a0567, прежние наблюдения,
selection/workplane context. Все71 protected file SHA/size совпали до/после.
Это point-in-time observations и проверка saved bytes, не proof полной topology.

Доказательства: evidence/K6-directory-fix/deployment.json,
deployment-independent-verification.json, deployment-context-before/after.json,
protected-before/after-deployment.json, deployment-report-test.json.
K6 SDK0.36.3-dev package и default Codex config не менялись.

Установка завершена. Новый live сценарий остановился после одного SaveAs:
model ID сменился в сохранённом прежнем окне, guard оставил outcome unknown.
STATUS-K6-SAVE-MODEL-IDENTITY.md; repeat SaveAs/OPEN/fence cleanup запрещены.
