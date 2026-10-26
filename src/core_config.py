"""General core configuration policy; legacy fixture profile remains opt-in."""
import datetime
import ntpath
import re

FIXTURE_FLAGS = ('translationTestPanel', 'checkpointTestPanel',
                 'parameterizedTranslationPanel', 'diskVerificationPanel',
                 'mcpMutationsPanel', 'recipeOperationsPanel', 'captureViewport')
FIXTURE_FIELDS = ('expectedProject', 'sourceStep', 'protectedProject',
                  'qualificationOperationId', 'qualificationReceiptSha256',
                  'qualificationReceiptRelease', 'testProjectSha256',
                  'protectedProjectSha256')


def general_core(settings):
    profile = settings.get('coreProfile')
    access = settings.get('ownedOpenAccessRestoreExperimental', False)
    if type(access) is not bool or access and (profile != 'general-v1' or any(settings.get(k) is not True for k in ('workingCopyOpenExperimental', 'workingCopiesExperimental', 'entitySetsExperimental', 'documentBindingsExperimental'))):
        raise ValueError('Owned OPEN current-access restore requires strict guarded dependencies')
    pin = settings.get('powerShapeProcessPin')
    if pin is not None:
        if profile != 'general-v1' or type(pin) is not dict or set(pin) != {'processId', 'processStartedUtc', 'windowsSessionId', 'executablePath'}:
            raise ValueError('Exact general PowerShape process pin required')
        if type(pin['processId']) is not int or not 0 < pin['processId'] <= 2147483647 or type(pin['windowsSessionId']) is not int or not 0 <= pin['windowsSessionId'] <= 2147483647:
            raise ValueError('Strict PID/session integers required')
        stamp, path = pin['processStartedUtc'], pin['executablePath']
        if type(stamp) is not str or not re.fullmatch(r'\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)', stamp):
            raise ValueError('Explicit UTC process creation time required')
        datetime.datetime.fromisoformat(stamp.replace('Z', '+00:00'))
        if type(path) is not str or len(path) > 1024 or not re.match(r'^[A-Za-z]:\\', path) or ntpath.normpath(path).casefold() != path.casefold() or ntpath.basename(path).casefold() != 'powershape.exe' or '\0' in path:
            raise ValueError('Canonical absolute PowerShape executable path required')
    levels = settings.get('levelReadingExperimental', False)
    if type(levels) is not bool or levels and profile != 'general-v1':
        raise ValueError('Passive level reading requires general core and a boolean flag')
    experimental = settings.get('entitySetsExperimental', False)
    plans = settings.get('operationPlansExperimental', False)
    copies = settings.get('workingCopiesExperimental', False)
    bindings = settings.get('documentBindingsExperimental', False)
    level_moves = settings.get('ownedLevelMovesExperimental', False)
    workplanes = settings.get('ownedWorkplanesExperimental', False)
    curve_rotation = settings.get('ownedCurveRotationsExperimental', False)
    if type(curve_rotation) is not bool or curve_rotation and (profile != 'general-v1' or settings.get('ownedRotationPlansExperimental') is not True or settings.get('curveWitnessesExperimental') is not True):
        raise ValueError('Curve rotations require owned rotation plans and combined passive witnesses')
    curve_move = settings.get('ownedCurveTranslationsExperimental', False)
    if type(curve_move) is not bool or curve_move and (profile != 'general-v1' or settings.get('ownedTranslationsExperimental') is not True or settings.get('curveLayoutExperimental') is not True):
        raise ValueError('Curve translation requires owned translations and curve layout')
    witnesses = settings.get('curveWitnessesExperimental', False)
    if type(witnesses) is not bool or witnesses and (profile != 'general-v1' or settings.get('curveLayoutExperimental') is not True):
        raise ValueError('Combined curve witnesses require passive curve layout')
    curve_layout = settings.get('curveLayoutExperimental', False)
    if type(curve_layout) is not bool or curve_layout and (profile != 'general-v1' or settings.get('entitySetsExperimental') is not True):
        raise ValueError('Curve layout requires general passive candidates')
    surface_samples = settings.get('surfaceSamplingExperimental', False)
    recovery = settings.get('curveScaleStopRecoveryExperimental', False)
    if type(recovery) is not bool or recovery and any(settings.get(k) is not True for k in ('curveScaleStopInspectionExperimental', 'ownedSelectionsExperimental')):
        raise ValueError('Curve scale stop recovery requires stop inspection and owned selection')
    stop = settings.get('curveScaleStopInspectionExperimental', False)
    if type(stop) is not bool or stop and settings.get('ownedCurveScalesExperimental') is not True:
        raise ValueError('Curve scale stop inspection needs strict curve scale gate')
    curve_scale = settings.get('ownedCurveScalesExperimental', False)
    if type(curve_scale) is not bool or curve_scale and (profile != 'general-v1' or any(settings.get(k) is not True for k in ('ownedUniformScalePlansExperimental', 'uniformScalePlansExperimental', 'operationPlansExperimental', 'curveWitnessesExperimental'))):
        raise ValueError('Curve scaling requires owned scale plans and passive curve/SOLID witnesses')
    surface_scale = settings.get('ownedSurfaceScalesExperimental', False)
    if type(surface_scale) is not bool or surface_scale and (profile != 'general-v1' or surface_samples is not True or any(settings.get(k) is not True for k in ('ownedUniformScalePlansExperimental', 'uniformScalePlansExperimental', 'operationPlansExperimental'))):
        raise ValueError('Surface scaling requires owned scale plans and passive surface sampling')
    surface_rotation = settings.get('ownedSurfaceRotationsExperimental', False)
    if type(surface_rotation) is not bool or surface_rotation and (profile != 'general-v1' or surface_samples is not True or settings.get('ownedRotationPlansExperimental') is not True):
        raise ValueError('Surface rotation requires owned rotation plans and sample flags')
    surface_set_translation = settings.get('ownedSurfaceSetTranslationsExperimental', False)
    if type(surface_set_translation) is not bool or surface_set_translation and (profile != 'general-v1' or settings.get('ownedSurfaceTranslationsExperimental') is not True or surface_samples is not True or settings.get('ownedTranslationsExperimental') is not True):
        raise ValueError('Surface set translation requires strict singleton translation and sampling dependencies')
    surface_translation = settings.get('ownedSurfaceTranslationsExperimental', False)
    if type(surface_translation) is not bool or surface_translation and (profile != 'general-v1' or surface_samples is not True or settings.get('ownedTranslationsExperimental') is not True):
        raise ValueError('Surface translation requires strict owned translation and sample flags')
    surface_selection = settings.get('ownedSurfaceSelectionsExperimental', False)
    if type(surface_selection) is not bool or surface_selection and (profile != 'general-v1' or surface_samples is not True or settings.get('ownedSelectionsExperimental') is not True):
        raise ValueError('Surface selection requires strict owned selection and sample flags')
    if type(surface_samples) is not bool or surface_samples and (profile != 'general-v1' or experimental is not True):
        raise ValueError('Surface sampling requires general passive candidates and boolean flag')
    selections = settings.get('ownedSelectionsExperimental', False)
    if type(selections) is not bool or selections and (profile != 'general-v1' or experimental is not True or copies is not True or bindings is not True):
        raise ValueError('Owned selection requires general copies/candidates/bindings and boolean flag')
    activation = settings.get('ownedWorkplaneActivationExperimental', False)
    if type(activation) is not bool or activation and (profile != 'general-v1' or experimental is not True or copies is not True or bindings is not True):
        raise ValueError('Owned workplane activation requires general sets/copies/bindings and a boolean flag')
    if type(workplanes) is not bool or workplanes and (profile != 'general-v1' or experimental is not True or copies is not True or bindings is not True):
        raise ValueError('Owned workplanes require general sets/copies/bindings and a boolean flag')
    if type(level_moves) is not bool or level_moves and (profile != 'general-v1' or levels is not True or experimental is not True or copies is not True or bindings is not True):
        raise ValueError('Owned level moves require general sets/copies/bindings/level reads')
    opens = settings.get('workingCopyOpenExperimental', False)
    adoption = settings.get('ownedOpenAdoptionExperimental', False)
    if type(adoption) is not bool or adoption and (profile != 'general-v1' or opens is not True or experimental is not True or copies is not True or bindings is not True):
        raise ValueError('Owned OPEN adoption requires guarded owned open and a boolean flag')
    owned_plans = settings.get('ownedOperationPlansExperimental', False)
    translations = settings.get('ownedTranslationsExperimental', False)
    save_targets = settings.get('ownedSaveTargetsExperimental', False)
    saves = settings.get('ownedSavesExperimental', False)
    reopens = settings.get('ownedSavedReopenExperimental', False)
    restore = settings.get('ownedSavedResultRestoreExperimental', False)
    scale_restore = settings.get('ownedSolidScaleRestoreExperimental', False)
    if type(scale_restore) is not bool or scale_restore and (profile != 'general-v1' or settings.get('ownedSolidScalesExperimental') is not True or reopens is not True or experimental is not True or copies is not True or bindings is not True):
        raise ValueError('Scale restoration requires qualified scale, saved reopen and boolean dependencies')
    result_close = settings.get('ownedSavedResultCloseExperimental', False)
    if type(result_close) is not bool or result_close and (profile != 'general-v1' or restore is not True or saves is not True or experimental is not True or copies is not True or bindings is not True):
        raise ValueError('Saved Result close requires current saved-result restoration and boolean dependencies')
    finalize = settings.get('ownedSavedFinalizationExperimental', False)
    if type(finalize) is not bool or finalize and (profile != 'general-v1' or restore is not True or reopens is not True or saves is not True or experimental is not True or copies is not True or bindings is not True):
        raise ValueError('Saved finalization requires sealed saved reopen and current saved-result restoration')
    close = settings.get('ownedSavedCopyCloseExperimental', False)
    if type(close) is not bool or close and (profile != 'general-v1' or reopens is not True or saves is not True or save_targets is not True or opens is not True or experimental is not True or copies is not True or bindings is not True):
        raise ValueError('Saved-copy close requires guarded saved reopen and boolean dependencies')
    window_limit = settings.get('savedReopenModelWindowLimit', 20)
    if type(window_limit) is not int or not 1 <= window_limit <= 32 or window_limit > 20 and restore is not True:
        raise ValueError('Expanded saved-reopen model quota requires current saved-result restoration; integer1..32')
    if type(restore) is not bool or restore and (profile != 'general-v1' or reopens is not True or saves is not True or save_targets is not True or opens is not True or experimental is not True or copies is not True or bindings is not True):
        raise ValueError('Saved-result restoration requires general guarded reopen and boolean dependencies')
    recovery = settings.get('checkpointRecoveryExperimental', False)
    owned_rotations = settings.get('ownedRotationPlansExperimental', False)
    solid_rotations = settings.get('ownedSolidRotationsExperimental', False)
    if type(solid_rotations) is not bool or solid_rotations and (profile != 'general-v1' or owned_rotations is not True or plans is not True):
        raise ValueError('Solid rotations require general owned rotation and operation plans')
    quarter_turns = settings.get('ownedSolidQuarterTurnsExperimental', False)
    if type(quarter_turns) is not bool or quarter_turns and (profile != 'general-v1' or owned_rotations is not True or plans is not True):
        raise ValueError('Solid quarter turns require general owned rotation and operation plans')
    owned_scales = settings.get('ownedUniformScalePlansExperimental', False)
    solid_scales = settings.get('ownedSolidScalesExperimental', False)
    if type(solid_scales) is not bool or solid_scales and (profile != 'general-v1' or owned_scales is not True or plans is not True or settings.get('uniformScalePlansExperimental') is not True):
        raise ValueError('Owned SOLID scales require general owned uniform scale and operation plans')
    dgk_targets = settings.get('ownedDgkTargetsExperimental', False)
    if type(dgk_targets) is not bool or dgk_targets and (profile != 'general-v1' or copies is not True or bindings is not True or experimental is not True):
        raise ValueError('Owned DGK targets require general sets, copies and bindings')
    if type(owned_scales) is not bool or owned_scales and (profile != 'general-v1' or copies is not True or bindings is not True or experimental is not True):
        raise ValueError('Owned uniform scale preparation requires general sets, copies and bindings')
    if type(owned_rotations) is not bool or owned_rotations and (profile != 'general-v1' or copies is not True or bindings is not True or experimental is not True):
        raise ValueError('Owned rotation preparation requires general sets, copies and bindings')
    if type(recovery) is not bool or recovery and (profile != 'general-v1' or copies is not True):
        raise ValueError('Checkpoint recovery requires general owned working copies')
    if type(reopens) is not bool or reopens and (profile != 'general-v1' or saves is not True or opens is not True):
        raise ValueError('Experimental saved reopen requires owned SaveAs and owned copy open')
    if type(saves) is not bool or saves and (profile != 'general-v1' or save_targets is not True):
        raise ValueError('Experimental SaveAs execution requires prepared owned targets')
    if type(save_targets) is not bool or save_targets and (profile != 'general-v1' or bindings is not True or copies is not True or experimental is not True):
        raise ValueError('Experimental SaveAs targets require general copies, candidates and bindings')
    if type(translations) is not bool or translations and (profile != 'general-v1' or owned_plans is not True):
        raise ValueError('Experimental owned translations require general coupled plans')
    if type(owned_plans) is not bool or owned_plans and (profile != 'general-v1' or plans is not True or copies is not True or experimental is not True or bindings is not True):
        raise ValueError('Experimental coupled plans require general core, local plans, sets, copies and bindings')
    if type(opens) is not bool or opens and (profile != 'general-v1' or copies is not True or experimental is not True or bindings is not True):
        raise ValueError('Experimental owned open requires general core, copies, candidates and bindings')
    if type(bindings) is not bool or bindings and (profile != 'general-v1' or copies is not True or experimental is not True):
        raise ValueError('Experimental document bindings require general core, working copies and entity candidates')
    if type(copies) is not bool or copies and profile != 'general-v1':
        raise ValueError('Experimental working copies require general core')
    if type(plans) is not bool or plans and (profile != 'general-v1' or experimental is not True):
        raise ValueError('Experimental operation plans require general core and entity sets')
    if type(experimental) is not bool or experimental and profile != 'general-v1':
        raise ValueError('Experimental entity sets require general core')
    if profile is not None and profile != 'general-v1':
        raise ValueError('Unknown core profile')
    if profile is None:
        return False
    if any(settings.get(key) not in (None, False) for key in FIXTURE_FLAGS):
        raise ValueError('General core forbids fixture flags and captures')
    if any(settings.get(key) not in (None, '') for key in FIXTURE_FIELDS):
        raise ValueError('General core forbids fixture paths and receipts')
    if settings.get('expectedVersion') != '23.2.2':
        raise ValueError('Unsupported target version')
    for key, lower, upper in (('timeoutSeconds', 1, 120), ('maxEntities', 1, 100000),
                               ('operationTimeoutSeconds', 1, 110)):
        value = settings.get(key)
        if type(value) is not int or not lower <= value <= upper:
            raise ValueError('Invalid core limit: ' + key)
    return True
