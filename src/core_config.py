"""General core configuration policy; legacy fixture profile remains opt-in."""
FIXTURE_FLAGS = ('translationTestPanel', 'checkpointTestPanel',
                 'parameterizedTranslationPanel', 'diskVerificationPanel',
                 'mcpMutationsPanel', 'recipeOperationsPanel', 'captureViewport')
FIXTURE_FIELDS = ('expectedProject', 'sourceStep', 'protectedProject',
                  'qualificationOperationId', 'qualificationReceiptSha256',
                  'qualificationReceiptRelease', 'testProjectSha256',
                  'protectedProjectSha256')


def general_core(settings):
    profile = settings.get('coreProfile')
    levels = settings.get('levelReadingExperimental', False)
    if type(levels) is not bool or levels and profile != 'general-v1':
        raise ValueError('Passive level reading requires general core and a boolean flag')
    experimental = settings.get('entitySetsExperimental', False)
    plans = settings.get('operationPlansExperimental', False)
    copies = settings.get('workingCopiesExperimental', False)
    bindings = settings.get('documentBindingsExperimental', False)
    opens = settings.get('workingCopyOpenExperimental', False)
    owned_plans = settings.get('ownedOperationPlansExperimental', False)
    translations = settings.get('ownedTranslationsExperimental', False)
    save_targets = settings.get('ownedSaveTargetsExperimental', False)
    saves = settings.get('ownedSavesExperimental', False)
    reopens = settings.get('ownedSavedReopenExperimental', False)
    recovery = settings.get('checkpointRecoveryExperimental', False)
    owned_rotations = settings.get('ownedRotationPlansExperimental', False)
    owned_scales = settings.get('ownedUniformScalePlansExperimental', False)
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
