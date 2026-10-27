"""Profile isolation and routing; no PowerShape connection."""
import json
from pathlib import Path
import uuid
import threading
import unittest

from core_config import general_core, FIXTURE_FIELDS, FIXTURE_FLAGS
from mcp_server import Server
from test_entity_references import snapshot


class CoreConfiguration(unittest.TestCase):
    def test_numeric_normal_set_separate_gate(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-surface-set-numerical.json').read_text())
        self.assertIs(settings['ownedSurfaceSetNumericalExperimental'], False)
        deps = ('ownedSurfaceSetTranslationsExperimental', 'ownedSurfaceTranslationsExperimental', 'surfaceSamplingExperimental', 'ownedTranslationsExperimental', 'ownedOperationPlansExperimental', 'operationPlansExperimental', 'entitySetsExperimental', 'workingCopiesExperimental', 'documentBindingsExperimental')
        enabled = {**settings, **{k: True for k in deps}, 'ownedSurfaceSetNumericalExperimental': True}
        general_core(enabled)
        for k in deps:
            with self.subTest(dependency=k), self.assertRaises(ValueError): general_core({**enabled, k: False})
        with self.assertRaises(ValueError): general_core({**enabled, 'ownedSurfaceSetNumericalExperimental': 'true'})

    def test_surface_set_disposition_gate(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-surface-set-stop-disposition.json').read_text())
        self.assertIs(settings['surfaceSetStopDispositionExperimental'], False)
        dependencies = ('surfaceSetStopInspectionExperimental', 'ownedSurfaceSetTranslationsExperimental', 'ownedSurfaceTranslationsExperimental', 'surfaceSamplingExperimental', 'ownedTranslationsExperimental', 'ownedOperationPlansExperimental', 'operationPlansExperimental', 'entitySetsExperimental', 'workingCopiesExperimental', 'documentBindingsExperimental')
        enabled = {**settings, **{k: True for k in dependencies}, 'surfaceSetStopDispositionExperimental': True}
        general_core(enabled)
        for k in dependencies:
            with self.subTest(dependency=k), self.assertRaises(ValueError): general_core({**enabled, k: False})
        with self.assertRaises(ValueError): general_core({**enabled, 'surfaceSetStopDispositionExperimental': 'true'})

    def test_surface_set_partial_stop_has_separate_strict_boolean_gate(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-surface-set-partial-stop.json').read_text())
        self.assertIs(settings['surfaceSetStopInspectionExperimental'], False)
        dependencies = ('ownedSurfaceSetTranslationsExperimental', 'ownedSurfaceTranslationsExperimental', 'surfaceSamplingExperimental', 'ownedTranslationsExperimental', 'ownedOperationPlansExperimental', 'operationPlansExperimental', 'entitySetsExperimental', 'workingCopiesExperimental', 'documentBindingsExperimental')
        enabled = {**settings, **{k: True for k in dependencies}, 'surfaceSetStopInspectionExperimental': True}
        general_core(enabled)
        for k in dependencies:
            with self.subTest(dependency=k), self.assertRaises(ValueError): general_core({**enabled, k: False})
        with self.assertRaises(ValueError): general_core({**enabled, 'surfaceSetStopInspectionExperimental': 'true'})

    def test_owned_open_access_restore_requires_explicit_boolean_and_guards(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-owned-open-access-restore.json').read_text())
        self.assertIs(settings['ownedOpenAccessRestoreExperimental'], False)
        enabled = {**settings, 'ownedOpenAccessRestoreExperimental': True,
                   **{k: True for k in ('workingCopyOpenExperimental', 'workingCopiesExperimental', 'entitySetsExperimental', 'documentBindingsExperimental')}}
        general_core(enabled)
        for k in ('workingCopyOpenExperimental', 'workingCopiesExperimental', 'entitySetsExperimental', 'documentBindingsExperimental'):
            with self.subTest(dependency=k), self.assertRaises(ValueError): general_core({**enabled, k: False})
        with self.assertRaises(ValueError): general_core({**enabled, 'ownedOpenAccessRestoreExperimental': 'true'})

    def test_explicit_process_pin_rejects_partial_or_coerced_identity(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-core.json').read_text())
        pin = dict(processId=101, processStartedUtc='2026-10-08T08:38:27.4633424Z', windowsSessionId=1, executablePath=r'C:\Program Files\Autodesk\PowerShape 2023\sys\exec64\powershape.exe')
        general_core({**settings, 'powerShapeProcessPin': pin})
        general_core({**settings, 'powerShapeProcessPin': None})
        bad = [True, {**pin, 'processId': True}, {**pin, 'windowsSessionId': '1'}, {**pin, 'fallback': True}, {k:v for k,v in pin.items() if k != 'processStartedUtc'}, {**pin, 'processStartedUtc': '2026-10-08T08:38:27'}, {**pin, 'executablePath': 'powershape.exe'}]
        for value in bad:
            with self.subTest(value=value), self.assertRaises(ValueError): general_core({**settings, 'powerShapeProcessPin': value})

    def test_curve_scale_stop_recovery_has_strict_separate_gate(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-curve-scale-stop-recovery.json').read_text())
        self.assertIs(settings['curveScaleStopRecoveryExperimental'], False)
        enabled = {**settings, 'ownedCurveScalesExperimental': True, 'curveScaleStopInspectionExperimental': True, 'curveScaleStopRecoveryExperimental': True}
        general_core(enabled)
        for value in ('true', 1, None):
            with self.subTest(value=value), self.assertRaises(ValueError): general_core({**enabled, 'curveScaleStopRecoveryExperimental': value})
        for field in ('curveScaleStopInspectionExperimental', 'ownedSelectionsExperimental'):
            with self.subTest(field=field), self.assertRaises(ValueError): general_core({**enabled, field: False})

    def test_curve_scale_stop_inspection_is_strict_and_separate(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-curve-scale-stop.json').read_text())
        self.assertIs(settings['curveScaleStopInspectionExperimental'], False)
        general_core({**settings, 'ownedCurveScalesExperimental': True, 'curveScaleStopInspectionExperimental': True})
        for value in ('true', 1, None):
            with self.subTest(value=value), self.assertRaises(ValueError): general_core({**settings, 'ownedCurveScalesExperimental': True, 'curveScaleStopInspectionExperimental': value})
        with self.assertRaises(ValueError): general_core({**settings, 'curveScaleStopInspectionExperimental': True})

    def test_curve_scale_requires_strict_separate_gate(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-curve-scales.json').read_text())
        self.assertIs(settings['ownedCurveScalesExperimental'], False)
        general_core({**settings, 'ownedCurveScalesExperimental': True})
        for value in ('true', 1, None):
            with self.subTest(value=value), self.assertRaises(ValueError): general_core({**settings, 'ownedCurveScalesExperimental': value})
        for field in ('ownedUniformScalePlansExperimental', 'uniformScalePlansExperimental', 'operationPlansExperimental', 'curveWitnessesExperimental'):
            with self.subTest(field=field), self.assertRaises(ValueError): general_core({**settings, 'ownedCurveScalesExperimental': True, field: False})

    def test_curve_rotation_has_strict_separate_gate(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-curve-rotations.json').read_text())
        self.assertIs(settings['ownedCurveRotationsExperimental'], False)
        settings.update(ownedCurveRotationsExperimental=True, rotationPlansExperimental=True, ownedRotationPlansExperimental=True, documentBindingsExperimental=True)
        general_core(settings)
        for value in ('true', 1, None):
            with self.subTest(value=value), self.assertRaises(ValueError): general_core({**settings, 'ownedCurveRotationsExperimental': value})
        for field in ('ownedRotationPlansExperimental', 'curveWitnessesExperimental'):
            with self.subTest(field=field), self.assertRaises(ValueError): general_core({**settings, field: False})

    def test_combined_curve_witnesses_require_passive_gate(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-curve-witnesses.json').read_text())
        self.assertIs(settings['curveWitnessesExperimental'], False)
        general_core({**settings, 'curveWitnessesExperimental': True})
        for value in ('true', 1, None):
            with self.subTest(value=value), self.assertRaises(ValueError): general_core({**settings, 'curveWitnessesExperimental': value})
        with self.assertRaises(ValueError): general_core({**settings, 'curveWitnessesExperimental': True, 'curveLayoutExperimental': False})

    def test_curve_translation_requires_strict_gate(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-curve-translations.json').read_text())
        self.assertIs(settings['ownedCurveTranslationsExperimental'], False)
        settings.update(ownedCurveTranslationsExperimental=True, curveLayoutExperimental=True, ownedTranslationsExperimental=True, ownedOperationPlansExperimental=True, operationPlansExperimental=True)
        general_core(settings)
        for value in ('true', 1, None):
            with self.subTest(value=value), self.assertRaises(ValueError): general_core({**settings, 'ownedCurveTranslationsExperimental': value})
        for field in ('ownedTranslationsExperimental', 'curveLayoutExperimental'):
            with self.subTest(field=field), self.assertRaises(ValueError): general_core({**settings, field: False})

    def test_curve_layout_requires_strict_passive_gate(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-curve-layout.json').read_text())
        self.assertIs(settings['curveLayoutExperimental'], False)
        general_core({**settings, 'curveLayoutExperimental': True})
        for value in ('true', 1, None):
            with self.subTest(value=value), self.assertRaises(ValueError): general_core({**settings, 'curveLayoutExperimental': value})
        with self.assertRaises(ValueError): general_core({**settings, 'curveLayoutExperimental': True, 'entitySetsExperimental': False})

    def test_surface_scale_requires_strict_separate_gate(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-surface-scale.json').read_text())
        self.assertIs(settings['ownedSurfaceScalesExperimental'], False)
        general_core(settings)
        settings['ownedSurfaceScalesExperimental'] = True
        general_core(settings)
        for value in ('true', 1, None):
            with self.subTest(value=value), self.assertRaises(ValueError): general_core({**settings, 'ownedSurfaceScalesExperimental': value})
        for field in ('ownedUniformScalePlansExperimental', 'uniformScalePlansExperimental', 'surfaceSamplingExperimental', 'operationPlansExperimental'):
            with self.subTest(field=field), self.assertRaises(ValueError): general_core({**settings, field: False})

    def test_surface_rotation_requires_strict_separate_gate(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-surface-rotations.json').read_text())
        self.assertIs(settings['ownedSurfaceRotationsExperimental'], False)
        general_core(settings)
        settings.update(ownedSurfaceRotationsExperimental=True, rotationPlansExperimental=True, operationPlansExperimental=True, ownedRotationPlansExperimental=True)
        general_core(settings)
        for value in ('true', 1, None):
            with self.subTest(value=value), self.assertRaises(ValueError): general_core({**settings, 'ownedSurfaceRotationsExperimental': value})
        for field in ('ownedRotationPlansExperimental', 'surfaceSamplingExperimental', 'entitySetsExperimental'):
            with self.subTest(field=field), self.assertRaises(ValueError): general_core({**settings, field: False})

    def test_surface_translation_requires_strict_separate_gate(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-surface-translations.json').read_text())
        self.assertIs(settings['ownedSurfaceTranslationsExperimental'], False)
        general_core(settings)
        settings.update(ownedSurfaceTranslationsExperimental=True, ownedTranslationsExperimental=True, operationPlansExperimental=True, ownedOperationPlansExperimental=True)
        general_core(settings)
        for value in ('true', 1, None):
            with self.subTest(value=value), self.assertRaises(ValueError): general_core({**settings, 'ownedSurfaceTranslationsExperimental': value})
        for field in ('ownedTranslationsExperimental', 'surfaceSamplingExperimental', 'entitySetsExperimental'):
            with self.subTest(field=field), self.assertRaises(ValueError): general_core({**settings, field: False})

    def test_surface_set_translation_requires_strict_gate_and_dependencies(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-surface-set-translations.json').read_text())
        self.assertIs(settings['ownedSurfaceSetTranslationsExperimental'], False)
        general_core(settings)
        settings.update(ownedSurfaceSetTranslationsExperimental=True, ownedSurfaceTranslationsExperimental=True, ownedTranslationsExperimental=True, operationPlansExperimental=True, ownedOperationPlansExperimental=True)
        general_core(settings)
        for value in ('true', 1, None):
            with self.subTest(value=value), self.assertRaises(ValueError): general_core({**settings, 'ownedSurfaceSetTranslationsExperimental': value})
        for field in ('ownedSurfaceTranslationsExperimental', 'ownedTranslationsExperimental', 'surfaceSamplingExperimental'):
            with self.subTest(field=field), self.assertRaises(ValueError): general_core({**settings, field: False})

    def test_surface_sampling_is_strict_passive_default_false(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-surface-sampling.json').read_text())
        self.assertIs(settings['surfaceSamplingExperimental'], False)
        general_core(settings)
        settings['surfaceSamplingExperimental'] = True
        general_core(settings)
        for value in ('true', 1, None):
            with self.subTest(value=value), self.assertRaises(ValueError): general_core({**settings, 'surfaceSamplingExperimental': value})
        with self.assertRaises(ValueError): general_core({**settings, 'entitySetsExperimental': False})

    def test_surface_selection_profile_requires_strict_separate_gate(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-surface-selection.json').read_text())
        self.assertIs(settings['ownedSurfaceSelectionsExperimental'], False)
        general_core(settings)
        settings.update(ownedSurfaceSelectionsExperimental=True, ownedSelectionsExperimental=True)
        general_core(settings)
        for value in ('true', 1, None):
            with self.subTest(value=value), self.assertRaises(ValueError): general_core({**settings, 'ownedSurfaceSelectionsExperimental': value})
        for field in ('ownedSelectionsExperimental', 'surfaceSamplingExperimental', 'entitySetsExperimental'):
            with self.subTest(field=field), self.assertRaises(ValueError): general_core({**settings, field: False})

    def test_owned_selection_full_only_flag_is_strict_and_default_false(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-owned-selection.json').read_text())
        self.assertIs(settings['ownedSelectionsExperimental'], False)
        general_core(settings)
        settings['ownedSelectionsExperimental'] = True
        general_core(settings)
        for value in ('true', 1, None):
            with self.subTest(value=value), self.assertRaises(ValueError): general_core({**settings, 'ownedSelectionsExperimental': value})
        for field in ('entitySetsExperimental', 'workingCopiesExperimental', 'documentBindingsExperimental'):
            with self.subTest(field=field), self.assertRaises(ValueError): general_core({**settings, field: False})

    def test_saved_finalization_keeps_distinct_strict_boolean_profile(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-owned-saved-finalization.json').read_text(encoding='utf-8-sig'))
        self.assertIs(settings['ownedSavedFinalizationExperimental'], False)
        self.assertIs(settings['ownedSavedResultCloseExperimental'], False)
        general_core(settings)
        settings['ownedSavedFinalizationExperimental'] = True
        general_core(settings)
        for value in ('true', 1, None):
            with self.subTest(value=value), self.assertRaises(ValueError): general_core({**settings, 'ownedSavedFinalizationExperimental': value})
        for field in ('ownedSavedResultRestoreExperimental', 'ownedSavedReopenExperimental', 'workingCopiesExperimental'):
            with self.subTest(field=field), self.assertRaises(ValueError): general_core({**settings, field: False})

    def test_saved_result_close_strict_boolean_dependencies(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-owned-saved-result-close.json').read_text(encoding='utf-8-sig'))
        general_core(settings)
        settings['ownedSavedResultCloseExperimental'] = True
        general_core(settings)
        for value in ('true', 1, None):
            with self.subTest(value=value), self.assertRaises(ValueError): general_core({**settings, 'ownedSavedResultCloseExperimental': value})
        for field in ('ownedSavedResultRestoreExperimental', 'ownedSavesExperimental', 'workingCopiesExperimental'):
            with self.subTest(field=field), self.assertRaises(ValueError): general_core({**settings, field: False})

    def test_scale_restore_flag_and_dependencies_are_strict(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-owned-solid-scale-restore.json').read_text(encoding='utf-8-sig'))
        general_core(settings)
        for value in ('true', 1, None):
            with self.subTest(value=value), self.assertRaises(ValueError): general_core({**settings, 'ownedSolidScaleRestoreExperimental': value})
        for dependency in ('ownedSolidScalesExperimental', 'ownedSavedReopenExperimental', 'workingCopiesExperimental', 'documentBindingsExperimental'):
            with self.subTest(dependency=dependency), self.assertRaises(ValueError): general_core({**settings, dependency: False})

    def test_saved_copy_close_flag_is_strict_full_sdk_only(self):
        settings = json.loads((Path(__file__).parents[1] / 'config/general-owned-saved-copy-close.json').read_text(encoding='utf-8-sig'))
        general_core(settings)
        for value in ('true', 1, None):
            with self.subTest(value=value), self.assertRaises(ValueError): general_core({**settings, 'ownedSavedCopyCloseExperimental': value})
        for dependency in ('ownedSavedReopenExperimental', 'workingCopiesExperimental', 'documentBindingsExperimental'):
            with self.subTest(dependency=dependency), self.assertRaises(ValueError): general_core({**settings, dependency: False})

    def setUp(self):
        self.settings = json.loads((Path(__file__).parents[1] / 'config/general-core.json').read_text())

    def test_fixture_options_and_unknown_profile_fail_closed(self):
        for key in FIXTURE_FIELDS + FIXTURE_FLAGS:
            with self.subTest(key=key), self.assertRaises(ValueError):
                general_core({**self.settings, key: True if key in FIXTURE_FLAGS else 'fixture'})
        with self.assertRaises(ValueError): general_core({**self.settings, 'coreProfile': 'typo'})

    def test_invalid_limits_rejected(self):
        for key in ('maxEntities', 'timeoutSeconds', 'operationTimeoutSeconds'):
            for value in (True, 0, '20', 100001):
                with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                    general_core({**self.settings, key: value})

    def test_saved_reopen_window_quota_is_bounded_and_requires_current_source(self):
        full = json.loads((Path(__file__).parents[1] / 'config/general-saved-result-restore.json').read_text())
        self.assertTrue(general_core({**full, 'savedReopenModelWindowLimit': 26}))
        for value in (True, '26', None, 0, 33, 26.5):
            with self.subTest(value=value), self.assertRaises(ValueError):
                general_core({**full, 'savedReopenModelWindowLimit': value})
        with self.assertRaises(ValueError):
            general_core({**full, 'savedReopenModelWindowLimit': 26, 'ownedSavedResultRestoreExperimental': False})

    def test_saved_result_restoration_has_strict_boolean_dependencies(self):
        full = json.loads((Path(__file__).parents[1] / 'config/general-saved-result-restore.json').read_text())
        self.assertTrue(general_core(full))
        for value in ('true', 1, None):
            with self.subTest(value=value), self.assertRaises(ValueError):
                general_core({**full, 'ownedSavedResultRestoreExperimental': value})
        for key in ('ownedSavedReopenExperimental', 'ownedSavesExperimental', 'ownedSaveTargetsExperimental',
                    'workingCopyOpenExperimental', 'entitySetsExperimental', 'workingCopiesExperimental', 'documentBindingsExperimental'):
            with self.subTest(key=key), self.assertRaises(ValueError):
                general_core({**full, key: False})

    def test_owned_uniform_scale_flag_requires_boolean_and_owned_dependencies(self):
        full = {**self.settings, 'entitySetsExperimental': True, 'workingCopiesExperimental': True,
                'documentBindingsExperimental': True, 'ownedUniformScalePlansExperimental': True}
        self.assertTrue(general_core(full))
        for value in ('true', 1, None):
            with self.subTest(value=value), self.assertRaises(ValueError):
                general_core({**full, 'ownedUniformScalePlansExperimental': value})
        for flag in ('entitySetsExperimental', 'workingCopiesExperimental', 'documentBindingsExperimental'):
            with self.subTest(flag=flag), self.assertRaises(ValueError):
                general_core({**full, flag: False})

    def test_owned_open_adoption_config_is_strict_and_sdk_only(self):
        valid = json.loads((Path(__file__).parents[1] / 'config/general-open-adoption.json').read_text())
        self.assertTrue(general_core(valid))
        for value in ('true', 1, None):
            with self.subTest(value=value), self.assertRaises(ValueError):
                general_core({**valid, 'ownedOpenAdoptionExperimental': value})
        for flag in ('workingCopyOpenExperimental', 'workingCopiesExperimental',
                     'entitySetsExperimental', 'documentBindingsExperimental'):
            with self.subTest(flag=flag), self.assertRaises(ValueError):
                general_core({**valid, flag: False})
        root = Path(__file__).parents[1] / 'work' / ('adoption-config-' + uuid.uuid4().hex)
        root.mkdir(parents=True)
        (root / 'bridge.config.json').write_text(json.dumps(valid))
        for read_only in (False, True):
            names = {tool['name'] for tool in Server(root, read_only=read_only).tools}
            self.assertFalse({'adopt_owned_open_document', 'restore_owned_open_adoption'} & names)
        self.assertFalse((root / 'requests').exists())

    def test_owned_dgk_flag_is_strict_and_does_not_add_legacy_python_tools(self):
        full = {**self.settings, 'entitySetsExperimental': True, 'workingCopiesExperimental': True,
                'documentBindingsExperimental': True, 'ownedDgkTargetsExperimental': True}
        self.assertTrue(general_core(full))
        for value in ('true', 1, None):
            with self.subTest(value=value), self.assertRaises(ValueError):
                general_core({**full, 'ownedDgkTargetsExperimental': value})
        for flag in ('entitySetsExperimental', 'workingCopiesExperimental', 'documentBindingsExperimental'):
            with self.subTest(flag=flag), self.assertRaises(ValueError):
                general_core({**full, flag: False})

    def test_save_flags_require_boolean_and_owned_dependencies(self):
        for key in ('ownedSaveTargetsExperimental', 'ownedSavesExperimental'):
            for value in (True, 'true', 1, None):
                with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                    general_core({**self.settings, key: value})
        prepared = {**self.settings, 'entitySetsExperimental': True,
                    'workingCopiesExperimental': True, 'documentBindingsExperimental': True,
                    'ownedSaveTargetsExperimental': True}
        self.assertTrue(general_core(prepared))
        self.assertTrue(general_core({**prepared, 'ownedSavesExperimental': True}))

    def test_solid_quarter_turn_flag_is_strict_without_legacy_mutation_facades(self):
        valid = json.loads((Path(__file__).parents[1] / 'config/general-solid-quarter-turn.json').read_text())
        self.assertTrue(general_core(valid))
        for value in (1, 'true', None):
            with self.subTest(value=value), self.assertRaises(ValueError):
                general_core({**valid, 'ownedSolidQuarterTurnsExperimental': value})
        for field in ('ownedRotationPlansExperimental', 'operationPlansExperimental'):
            with self.subTest(field=field), self.assertRaises(ValueError):
                general_core({**valid, field: False})
        root = Path(__file__).parents[1] / 'work' / ('quarter-turn-config-' + uuid.uuid4().hex)
        root.mkdir(parents=True)
        (root / 'bridge.config.json').write_text(json.dumps(valid))
        for read_only in (False, True):
            names = {tool['name'] for tool in Server(root, read_only=read_only).tools}
            self.assertFalse({'prepare_owned_solid_quarter_turn', 'execute_owned_solid_quarter_turn',
                              'verify_owned_solid_quarter_turn'} & names)
        self.assertFalse((root / 'requests').exists())

    def test_native_save_profile_does_not_enable_python_save_facades(self):
        root = Path(__file__).parents[1] / 'work' / ('save-profile-test-' + uuid.uuid4().hex)
        root.mkdir(parents=True)
        settings = json.loads((Path(__file__).parents[1] / 'config/general-owned-saves.json').read_text(encoding='utf-8-sig'))
        (root / 'bridge.config.json').write_text(json.dumps(settings))
        names = {tool['name'] for tool in Server(root).tools}
        self.assertFalse({'prepare_owned_save_as', 'execute_owned_save_as', 'verify_owned_save_as'} & names)
        self.assertEqual(len(names), 21)
        self.assertEqual(len(Server(root, read_only=True).tools), 6)

    def test_maintenance_refuses_queue_submission_without_cad(self):
        root = Path(__file__).parents[1] / 'work' / ('maintenance-test-' + uuid.uuid4().hex)
        root.mkdir(parents=True)
        (root / 'bridge.config.json').write_text(json.dumps(self.settings))
        (root / 'maintenance.json').write_text('{}')
        server = Server(root)
        self.assertTrue(server.status()['maintenance'])
        result = server.snapshot(threading.Event(), 'recipe.prepare', {'recipeId': 'inspect_active_model', 'parameters': {}})
        self.assertEqual(result['state'], 'maintenance_before_submission')
        self.assertFalse((root / 'requests').exists())

    def test_core_catalog_and_read_only_do_not_require_fixture(self):
        root = Path(__file__).parents[1] / 'work' / ('core-test-' + uuid.uuid4().hex)
        root.mkdir(parents=True)
        with self.subTest(profile='general-v1'):
            Path(root, 'bridge.config.json').write_text(json.dumps(self.settings))
            server = Server(root)
            names = {t['name'] for t in server.tools}
            self.assertEqual(names, {'bridge_status', 'list_documents', 'activate_document',
                                     'inspect_active_model', 'resolve_entity_references', 'operation_result'})
            self.assertEqual({t['name'] for t in Server(root, read_only=True).tools}, names - {'activate_document'})
            self.assertEqual(server.status()['geometryMutations'], 'disabled')
            calls, replies = [], []
            def read(cancel, operation, args):
                calls.append(args['recipeId'])
                return snapshot()
            server.snapshot = read
            server.reply = lambda request, result=None, error=None: replies.append((result, error))
            server.call(1, {'name': 'inspect_active_model'}, threading.Event())
            self.assertEqual(calls, ['inspect_active_model'])
            self.assertEqual(replies[0][0]['structuredContent']['state'], 'active_model_inspected')
            server.call(2, {'name': 'execute_recipe', 'arguments': {}}, threading.Event())
            self.assertEqual(len(calls), 1)
            self.assertEqual(replies[-1][1]['code'], -32602)


if __name__ == '__main__': unittest.main()
