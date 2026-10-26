"""PowerShape Bridge MCP stdio adapter, Python standard library only.

The already running user-session panel owns all PowerShape calls. This process
only publishes fixed snapshot requests and consumes their matching responses.
"""
import argparse
import base64
import hashlib
import json
import math
import re
import os
from pathlib import Path
import sys
import threading
import time
import uuid
from decimal import Decimal
from entity_references import ReferenceStore, ReferenceRejected
from document_handles import DocumentHandleStore
from entity_sets import EntitySetStore, token as entity_set_token
from operation_plans import OperationPlanStore, validate_arguments as validate_plan_arguments, journal_events
from working_copies import WorkingCopyStore, validate_source as validate_copy_source
from document_bindings import DocumentBindingStore
from working_copy_open import validate_arguments as validate_open_arguments, prepare_parameters as prepare_open_parameters, verify_receipt as verify_open_receipt
from working_copy_open import validate_verification_arguments, verification_parameters
from owned_operation_plans import OwnedOperationStore
from owned_translation import execution_receipt as translation_receipt, validate_verification as validate_translation_verification, verification as translation_verification, checked_verification as checked_translation_verification

VERSIONS = ('2025-11-25', '2025-06-18', '2025-03-26')
MAX_MESSAGE = 65536
ADAPTER_VERSION = '0.59.0-dev'
DOCUMENT_TOOLS = [
    {'name':'list_documents',
     'description':'Read and recheck native PowerShape windows and already-open model documents without activating, saving, closing, selecting entities or creating images. Returns process/start/session identity and one-use opaque documentHandle choices (300 seconds, same MCP connection). Windows are not persistent IDs; documents with multiple model windows cannot be activated by this version.',
     'inputSchema':{'type':'object','properties':{},'additionalProperties':False},
     'annotations':{'readOnlyHint':True,'destructiveHint':False,'openWorldHint':False}},
    {'name':'activate_document',
     'description':'Activate one already-open model using its unconsumed documentHandle from list_documents in the same connection. Rechecks the full native window listing in the worker immediately before one Sel_Window Front command. Does not open files, save/close models or issue geometry commands. Unknown outcome must not be retried automatically. May bring a CAD window to the front; unavailable in read-only mode.',
     'inputSchema':{'type':'object','properties':{'documentHandle':{'type':'string','pattern':'^[0-9a-f]{32}$'}},'required':['documentHandle'],'additionalProperties':False},
     'annotations':{'readOnlyHint':False,'destructiveHint':False,'idempotentHint':False,'openWorldHint':False}},
]
REFERENCE_TOOLS = [
    {'name': 'inspect_active_model',
     'description': 'Read the active supported PowerShape document without selecting, editing, saving or capturing an image. Accepts documents beyond the configured test block, including unsaved models. Returns inventory, available measured properties and opaque read references valid for 300 seconds in this MCP connection. References are not persistent and are not accepted by mutation tools. Requires the compatible user-session panel with recipe transport enabled.',
     'inputSchema': {'type': 'object', 'properties': {}, 'additionalProperties': False},
     'annotations': {'readOnlyHint': True, 'destructiveHint': False, 'openWorldHint': False}},
    {'name': 'resolve_entity_references',
     'description': 'Freshly inspect the active document and resolve read references issued by inspect_active_model in the same connection. Rejects expiry, unknown/cross-document references and changed observed document/properties/units. No selection or geometry commands. Does not certify full geometry identity, detect unobserved ID reuse, or authorize an edit.',
     'inputSchema': {'type': 'object', 'properties': {
         'documentReference': {'type': 'string', 'pattern': '^[0-9a-f]{32}$'},
         'entityReferences': {'type': 'array', 'items': {'type': 'string', 'pattern': '^[0-9a-f]{32}$'},
                              'minItems': 1, 'maxItems': 128, 'uniqueItems': True}},
         'required': ['documentReference', 'entityReferences'], 'additionalProperties': False},
     'annotations': {'readOnlyHint': True, 'destructiveHint': False, 'openWorldHint': False}},
]
SURFACE_PHASES = ('setup','slice','verify_surfaces','export','import','verify')
ENTITY_SET_TOOLS = [
    {'name': 'inspect_entity_candidates',
     'description': 'Experimental: freshly read active document and issue candidate handles separate from P2.21 read references. No selection, activation, save or capture. Handles expire within 300 seconds in this connection. No edit authorization or persistent/topological identity.',
     'inputSchema': {'type': 'object', 'properties': {}, 'additionalProperties': False},
     'annotations': {'readOnlyHint': True, 'destructiveHint': False, 'openWorldHint': False}},
    {'name': 'prepare_entity_set',
     'description': 'Experimental: prepare an immutable observed set of SOLID/SURFACE/COMPCURVE candidates in the explicit candidate document. Worker verifies original native receipt, expiry and fresh document/inventory/properties before writing a local set artifact. Does not edit CAD or accept P2.21 read references. No mutation tools accept these sets yet.',
     'inputSchema': {'type': 'object', 'properties': {
         'candidateDocumentHandle': {'type': 'string', 'pattern': '^[0-9a-f]{32}$'},
         'candidateEntityHandles': {'type': 'array', 'items': {'type': 'string', 'pattern': '^[0-9a-f]{32}$'}, 'minItems': 1, 'maxItems': 128, 'uniqueItems': True}},
         'required': ['candidateDocumentHandle', 'candidateEntityHandles'], 'additionalProperties': False},
     'annotations': {'readOnlyHint': False, 'destructiveHint': False, 'idempotentHint': False, 'openWorldHint': False}},
    {'name': 'validate_entity_set',
     'description': 'Experimental: freshly validate an observed set in the worker, using an opaque handle from this connection. Rechecks set artifact/native receipt hashes, lifetime and document/inventory/properties. No selection, activation, save or capture. A standalone check is not authorization or validation immediately before a later edit.',
     'inputSchema': {'type': 'object', 'properties': {'entitySetHandle': {'type': 'string', 'pattern': '^[0-9a-f]{32}$'}}, 'required': ['entitySetHandle'], 'additionalProperties': False},
     'annotations': {'readOnlyHint': True, 'destructiveHint': False, 'openWorldHint': False}},
]
WORKING_COPY_TOOLS = [
    {'name': 'prepare_working_copy',
     'description': 'Experimental saved-file operation: copy the explicit local .psmodel whose disk SHA256 matches into a new owned Working.psmodel and separate independent read-only Before.psmodel checkpoint. Records native file identity and immutable ownership receipt. Source is held read-only with writes/deletes denied; busy/changed sources are rejected without weaker sharing. No CAD connection, activation, SaveAs or unsaved-model capture. Copies are not certified loadable or accepted for geometry execution yet.',
     'inputSchema': {'type': 'object', 'properties': {'sourcePath': {'type': 'string'}, 'sourceSha256': {'type': 'string', 'pattern': '^[0-9a-f]{64}$'}}, 'required': ['sourcePath', 'sourceSha256'], 'additionalProperties': False},
     'annotations': {'readOnlyHint': False, 'destructiveHint': False, 'idempotentHint': False, 'openWorldHint': False}},
    {'name': 'inspect_working_copy',
     'description': 'Experimental local-file inspection using a workingCopyHandle issued in this connection (300 seconds). Checks ownership SHA, fixed paths, native file identities, no reparse/hard-link aliases and unchanged independent read-only checkpoint. Reports point-in-time working SHA/changed bytes. No CAD connection, activation or save; not an executor COM guard. Absent from read-only server because handles are issued only by preparation.',
     'inputSchema': {'type': 'object', 'properties': {'workingCopyHandle': {'type': 'string', 'pattern': '^[0-9a-f]{32}$'}}, 'required': ['workingCopyHandle'], 'additionalProperties': False},
     'annotations': {'readOnlyHint': True, 'destructiveHint': False, 'openWorldHint': False}},
]
OWNED_OPEN_TOOLS = [
    {'name': 'verify_working_copy_open',
     'description': 'Verify a returned owned-copy open without replay, using its original native openRequestId/receipt SHA and fresh candidateDocumentHandle plus one-use activeDocumentHandle. Worker checks plan/claim/journal, independently issued receipts, two same-COM model reads, ownership/checkpoint and preserved original saved files/windows. Successful verification appends succeeded_verified with actual evidence references and issues a fresh workingCopyHandle, including after reconnect. Supports the original owned-open window only; no open, save, close, activation, selection or geometry commands. Native WINDOW.ID may renumber during open and is not persistent.',
     'inputSchema': {'type': 'object', 'properties': {**{k: {'type': 'string', 'pattern': '^[0-9a-f]{32}$'} for k in ('openRequestId', 'candidateDocumentHandle', 'activeDocumentHandle')}, 'openReceiptSha256': {'type': 'string', 'pattern': '^[0-9a-f]{64}$'}}, 'required': ['openRequestId', 'openReceiptSha256', 'candidateDocumentHandle', 'activeDocumentHandle'], 'additionalProperties': False},
     'annotations': {'readOnlyHint': False, 'destructiveHint': False, 'idempotentHint': False, 'openWorldHint': False}},
    {'name': 'open_working_copy',
     'description': 'Experimental context-changing operation: open only an owned saved Working.psmodel in exactly one new model window. Requires this connection\'s workingCopyHandle, fresh candidateDocumentHandle and a one-use activeDocumentHandle from list_documents. Worker rechecks the current model/windows, copy/checkpoint and fixed parameters immediately before FILE OPENPATH / fixed PATHNAME / ACCEPT. Native durable plan/journal and per-copy open claim forbid replay. No save, close, entity selection, screenshot or geometry commands. Existing windows/files must be preserved; empty entity selection and ASCII installation path without spaces/quotes required. After any unknown outcome inspect operation_result/documents/files, never retry. New model becomes active; old CAD references are revoked. Read-only excludes this tool.',
     'inputSchema': {'type': 'object', 'properties': {k: {'type': 'string', 'pattern': '^[0-9a-f]{32}$'} for k in ('workingCopyHandle', 'candidateDocumentHandle', 'activeDocumentHandle')}, 'required': ['workingCopyHandle', 'candidateDocumentHandle', 'activeDocumentHandle'], 'additionalProperties': False},
     'annotations': {'readOnlyHint': False, 'destructiveHint': False, 'idempotentHint': False, 'openWorldHint': False}},
]
DOCUMENT_BINDING_TOOLS = [
    {'name': 'bind_working_document',
     'description': 'Experimental passive binding of the already-active owned Working.psmodel. Requires candidateDocumentHandle from a fresh inspection and workingCopyHandle in this connection. Rechecks the pinned receipt, two observations and native windows using one retained COM application/document, plus retained ownership/working/checkpoint file handles. Writes an immutable binding artifact; no open, activation, selection, save, screenshot or geometry command. Rejects source/checkpoint/foreign models. Not a later edit authorization or persistent lifecycle identity.',
     'inputSchema': {'type': 'object', 'properties': {'candidateDocumentHandle': {'type': 'string', 'pattern': '^[0-9a-f]{32}$'}, 'workingCopyHandle': {'type': 'string', 'pattern': '^[0-9a-f]{32}$'}}, 'required': ['candidateDocumentHandle', 'workingCopyHandle'], 'additionalProperties': False},
     'annotations': {'readOnlyHint': False, 'destructiveHint': False, 'idempotentHint': False, 'openWorldHint': False}},
    {'name': 'validate_working_document_binding',
     'description': 'Experimental passive validation of this connection\'s workingDocumentBindingHandle. Rechecks immutable binding/receipt hashes, expiry, ownership/checkpoint and active model/window observations using one retained COM context. No CAD commands; does not authorize an action after this standalone check. Read-only server does not issue binding handles.',
     'inputSchema': {'type': 'object', 'properties': {'workingDocumentBindingHandle': {'type': 'string', 'pattern': '^[0-9a-f]{32}$'}}, 'required': ['workingDocumentBindingHandle'], 'additionalProperties': False},
     'annotations': {'readOnlyHint': True, 'destructiveHint': False, 'openWorldHint': False}},
]
OPERATION_PLAN_TOOLS = [
    {'name': 'prepare_operation_plan',
     'description': 'Experimental local plan only: translate one observed entity set or intersect two disjoint sets (SOLID/SURFACE primary, SURFACE secondary). Pins set/source receipt SHA, identity, raw units, parameters and expiry; writes immutable plan and prepared journal. Does not contact CAD or execute commands. Execution remains disabled until owned-copy/checkpoint policy, worker guard and operation qualification are implemented.',
     'inputSchema': {'type': 'object', 'properties': {
         'operation': {'type': 'string', 'enum': ['translate_entities', 'intersect_entity_sets']},
         'entitySetHandles': {'type': 'array', 'items': {'type': 'string', 'pattern': '^[0-9a-f]{32}$'}, 'minItems': 1, 'maxItems': 2, 'uniqueItems': True},
         'parameters': {'oneOf': [
             {'type': 'object', 'properties': {'vector': {'type': 'array', 'items': {'type': 'number', 'minimum': -1000000, 'maximum': 1000000}, 'minItems': 3, 'maxItems': 3}, 'coordinateFrame': {'const': 'world'}, 'lengthUnits': {'const': 'document_raw'}, 'mode': {'const': 'move'}}, 'required': ['vector', 'coordinateFrame', 'lengthUnits', 'mode'], 'additionalProperties': False},
             {'type': 'object', 'properties': {'offset': {'const': 0}}, 'required': ['offset'], 'additionalProperties': False}]}},
         'required': ['operation', 'entitySetHandles', 'parameters'], 'additionalProperties': False},
     'annotations': {'readOnlyHint': False, 'destructiveHint': False, 'idempotentHint': False, 'openWorldHint': False}},
    {'name': 'inspect_operation_plan',
     'description': 'Experimental local check of this connection\'s plan handle, hashes, original native receipts, lease expiry and recorded journal. No live CAD read, command or edit authorization. Requires preparation in the same connection; absent from read-only server.',
     'inputSchema': {'type': 'object', 'properties': {'operationPlanHandle': {'type': 'string', 'pattern': '^[0-9a-f]{32}$'}}, 'required': ['operationPlanHandle'], 'additionalProperties': False},
     'annotations': {'readOnlyHint': True, 'destructiveHint': False, 'openWorldHint': False}},
]
SURFACE_TOOLS = [
 {'name':'prepare_test_surface_job','description':'Prepare an immutable local plan for the qualified four-wall surface fixture, fixed source level 1:Vneshnie 0.8 and 25 world XY planes Z=0..19.2 at h=0.8. Checks the previously qualified source file/receipt, creates plan JSON only; does not contact PowerShape or certify live model context. No arbitrary models accepted.',
  'inputSchema':{'type':'object','properties':{},'additionalProperties':False},'annotations':{'readOnlyHint':False,'destructiveHint':False,'openWorldHint':False}},
 {'name':'execute_test_surface_job_phase','description':'Execute one bounded phase of the immutable qualified surface-fixture plan. setup copies six source surfaces and assigns four walls to working level and caps to auxiliary level. slice intersects only SURFACE entities of the working level at one planned height and assigns new curves to the contour level. export creates one DGK containing every height; import and verify check that DGK on independent copies. Start on the configured baseline. Follow setup, all slice indices 0..24, verify_surfaces, export, import, all verify indices 0..24. Never retry an unknown outcome; no arbitrary Execute or production models.',
  'inputSchema':{'type':'object','properties':{'jobId':{'type':'string','pattern':'^[0-9a-f]{32}$'},'planSha256':{'type':'string','pattern':'^[0-9a-f]{64}$'},'phase':{'type':'string','enum':list(SURFACE_PHASES)},'index':{'type':'integer','minimum':0,'maximum':24}},'required':['jobId','planSha256','phase','index'],'additionalProperties':False},'annotations':{'readOnlyHint':False,'destructiveHint':True,'idempotentHint':False,'openWorldHint':False}},
]
OWNED_OPERATION_TOOLS = [
    {'name': 'prepare_owned_operation',
     'description': 'Experimental passive guard: couple this connection\'s immutable local operation plan and owned document binding. Native worker pins plan/set/receipt/binding/ownership/checkpoint bytes and checks one retained COM application/document twice before/after writing a disabled packet and prepared journal. No CAD commands, geometry execution or authorization for a later edit.',
     'inputSchema': {'type': 'object', 'properties': {'operationPlanHandle': {'type': 'string', 'pattern': '^[0-9a-f]{32}$'}, 'workingDocumentBindingHandle': {'type': 'string', 'pattern': '^[0-9a-f]{32}$'}}, 'required': ['operationPlanHandle', 'workingDocumentBindingHandle'], 'additionalProperties': False},
     'annotations': {'readOnlyHint': False, 'destructiveHint': False, 'idempotentHint': False, 'openWorldHint': False}},
    {'name': 'validate_owned_operation',
     'description': 'Experimental passive guard for this connection\'s disabled ownedOperationHandle: recheck every plan/set/binding dependency, working-byte hash, checkpoint and retained live COM context. Returns observations only; no execution or lasting edit authorization. No native ID/path/command arguments; absent from read-only server.',
     'inputSchema': {'type': 'object', 'properties': {'ownedOperationHandle': {'type': 'string', 'pattern': '^[0-9a-f]{32}$'}}, 'required': ['ownedOperationHandle'], 'additionalProperties': False},
     'annotations': {'readOnlyHint': True, 'destructiveHint': False, 'openWorldHint': False}},
]
OWNED_TRANSLATION_TOOLS = [
    {'name': 'execute_owned_translation',
     'description': 'Experimental explicit edit of one SOLID in an already active owned working copy. Consume this connection\'s ownedOperationHandle, repeat its guard in the retained executor COM context, then call direct TranslateItem once in world/document raw mm. No save, selection or inverse. Returns executed_unverified; the copy remains fenced until independent verify_owned_translation. Never retry an unknown outcome.',
     'inputSchema': {'type': 'object', 'properties': {'ownedOperationHandle': {'type': 'string', 'pattern': '^[0-9a-f]{32}$'}}, 'required': ['ownedOperationHandle'], 'additionalProperties': False},
     'annotations': {'readOnlyHint': False, 'destructiveHint': True, 'idempotentHint': False, 'openWorldHint': False}},
    {'name': 'verify_owned_translation',
     'description': 'Independently check an already returned owned translation by execution receipt ID/SHA and fresh same-connection candidate/active document choices. No CAD command. On observed success append succeeded_verified and release only this execution\'s copy fence; interrupted command or mismatch remains fenced. No save, inverse or replay.',
     'inputSchema': {'type': 'object', 'properties': {'executionRequestId': {'type': 'string', 'pattern': '^[0-9a-f]{32}$'}, 'executionReceiptSha256': {'type': 'string', 'pattern': '^[0-9a-f]{64}$'}, 'candidateDocumentHandle': {'type': 'string', 'pattern': '^[0-9a-f]{32}$'}, 'activeDocumentHandle': {'type': 'string', 'pattern': '^[0-9a-f]{32}$'}}, 'required': ['executionRequestId', 'executionReceiptSha256', 'candidateDocumentHandle', 'activeDocumentHandle'], 'additionalProperties': False},
     'annotations': {'readOnlyHint': False, 'destructiveHint': False, 'openWorldHint': False}}
]
OPERATION_RESULT_TOOL = {
    'name':'operation_result',
    'description':'Read a recorded operation journal by operationId without contacting PowerShape or retrying commands. Returns recorded state/phase and journal SHA256. Does not prove current model state or current hashes of model/export files.',
    'inputSchema':{'type':'object','properties':{'operationId':{'type':'string','pattern':'^[0-9a-f]{32}$'}},'required':['operationId'],'additionalProperties':False},
    'annotations':{'readOnlyHint':True,'destructiveHint':False,'openWorldHint':False},
}
TOOLS = [
    {'name': 'bridge_status', 'description': 'Read local Bridge configuration and last worker phase. Does not contact PowerShape. This is not proof the panel is alive.',
     'inputSchema': {'type': 'object', 'properties': {}, 'additionalProperties': False},
     'annotations': {'readOnlyHint': True, 'destructiveHint': False, 'openWorldHint': False}},
    {'name': 'model_snapshot', 'description': 'Request fresh data from the open configured PowerShape project via the running P1 panel. Reads entities, units and current-selection bounds. Depending on Bridge configuration also creates a JPEG in captures; does not modify geometry. Requires the panel to accept read requests. includeImage controls returning image bytes, not creation. Raw area units may be unresolved. Selection identity is only type/name, not a persistent object reference.',
     'inputSchema': {'type': 'object', 'properties': {'includeImage': {'type': 'boolean', 'default': False}}, 'additionalProperties': False},
     'annotations': {'readOnlyHint': False, 'destructiveHint': False, 'idempotentHint': False, 'openWorldHint': False}},
]
DISK_TOOL = {
    'name': 'verify_saved_operation',
    'description': 'Verify the one configured successful operation from disk. Creates independent copies, opens each test document in PowerShape, and closes its verified audit windows. Does not edit geometry. Original operation files are hash-checked before/after. Requires P2.4 panel. No arbitrary paths or operation IDs accepted.',
    'inputSchema': {'type': 'object', 'properties': {}, 'additionalProperties': False},
    'annotations': {'readOnlyHint': False, 'destructiveHint': False, 'idempotentHint': False, 'openWorldHint': False},
}
MUTATION_TOOLS = [
    {'name': 'activate_test_baseline', 'description': 'Activate/open the one configured test baseline. May open a new window; never saves or closes models. Only the qualified test block is allowed.',
     'inputSchema': {'type': 'object', 'properties': {}, 'additionalProperties': False},
     'annotations': {'readOnlyHint': False, 'destructiveHint': False, 'openWorldHint': False}},
    {'name': 'prepare_translation', 'description': 'Prepare a five-minute immutable translation plan for the active qualified baseline, one solid, world millimetres. Does not move geometry. Returns operationId and planSha256 for execution.',
     'inputSchema': {'type': 'object', 'properties': {'deltaWorldMm': {'type': 'array', 'items': {'type': 'number', 'minimum': -100, 'maximum': 100}, 'minItems': 3, 'maxItems': 3}}, 'required': ['deltaWorldMm'], 'additionalProperties': False},
     'annotations': {'readOnlyHint': False, 'destructiveHint': False, 'openWorldHint': False}},
    {'name': 'execute_translation', 'description': 'Execute exactly the prepared plan identified by operationId and planSha256. Changes the active test model, saves working/checkpoint/result files. Rechecks current measured properties and plan expiry. The checkpoint is detached and immutable. No automatic retry; a timeout may leave an unknown outcome. Restricted to the configured qualified test block.',
     'inputSchema': {'type': 'object', 'properties': {'operationId': {'type': 'string', 'pattern': '^[0-9a-f]{32}$'}, 'planSha256': {'type': 'string', 'pattern': '^[0-9a-f]{64}$'}}, 'required': ['operationId', 'planSha256'], 'additionalProperties': False},
     'annotations': {'readOnlyHint': False, 'destructiveHint': True, 'idempotentHint': False, 'openWorldHint': False}},
]
RECIPE_TOOLS = [
    {'name': 'prepare_recipe', 'description': 'Prepare a qualified baseline-cube recipe: rotate_z about the world origin, or assign_test_level (unused levels 900..909, fixed AI_Bridge_Test_N name, empty selection required). Returns immutable plan ID/digest. No model edits during preparation.',
     'inputSchema': {'type': 'object', 'properties': {'recipeId': {'type': 'string', 'enum': ['rotate_z', 'assign_test_level']}, 'parameters': {'oneOf': [{'type': 'object', 'properties': {'angleDegrees': {'type': 'integer', 'minimum': -180, 'maximum': 180}}, 'required': ['angleDegrees'], 'additionalProperties': False}, {'type': 'object', 'properties': {'levelNumber': {'type': 'integer', 'minimum': 900, 'maximum': 909}}, 'required': ['levelNumber'], 'additionalProperties': False}]}}, 'required': ['recipeId', 'parameters'], 'additionalProperties': False},
     'annotations': {'readOnlyHint': False, 'destructiveHint': False, 'openWorldHint': False}},
    {'name': 'execute_recipe', 'description': 'Execute the registered recipe bound to its immutable plan ID/digest. Creates working/checkpoint/result files, changes test geometry, verifies measured postconditions. Never retry an unknown outcome.',
     'inputSchema': MUTATION_TOOLS[-1]['inputSchema'],
     'annotations': {'readOnlyHint': False, 'destructiveHint': True, 'idempotentHint': False, 'openWorldHint': False}},
]

_workplane_vector = {'type': 'array', 'items': {'type': 'number'}, 'minItems': 3, 'maxItems': 3}
RECIPE_TOOLS[0]['inputSchema']['properties']['recipeId']['enum'].append('create_workplane_world')
RECIPE_TOOLS[0]['inputSchema']['properties']['parameters']['oneOf'].append({
    'type': 'object', 'properties': {key: _workplane_vector for key in ('originWorldMm', 'zAxis', 'yAxis')},
    'required': ['originWorldMm', 'zAxis', 'yAxis'], 'additionalProperties': False,
})
RECIPE_TOOLS[0]['description'] += ' create_workplane_world creates one world workplane from originWorldMm and orthogonal unit zAxis/yAxis, then restores world coordinates; origin is limited to +/-100 mm.'
RECIPE_TOOLS[0]['inputSchema']['properties']['recipeId']['enum'].append('scale_uniform_world')
RECIPE_TOOLS[0]['inputSchema']['properties']['parameters']['oneOf'].append({'type':'object','properties':{'scaleFactor':{'type':'number','minimum':0.5,'maximum':2,'not':{'const':1}}},'required':['scaleFactor'],'additionalProperties':False})
RECIPE_TOOLS[0]['description'] += ' scale_uniform_world uses a fixed Autodesk command recipe to scale the baseline solid about world zero by 0.5..2 (except 1); requires empty selection, restores it empty, verifies bounds/COG and cubic volume/quadratic area.'


RECIPE_TOOLS[0]['inputSchema']['properties']['recipeId']['enum'].append('convert_test_solid_to_surfaces')
RECIPE_TOOLS[0]['inputSchema']['properties']['parameters']['oneOf'].append({'type':'object','properties':{},'additionalProperties':False})
RECIPE_TOOLS[0]['description'] += ' convert_test_solid_to_surfaces has empty parameters; converts the qualified cube on a separate working copy into six independently checked surfaces.'

RECIPE_TOOLS[0]['inputSchema']['properties']['recipeId']['enum'].append('offset_test_top_surface_1mm')
RECIPE_TOOLS[0]['description'] += ' offset_test_top_surface_1mm converts the baseline cube and offsets only its top face outward by exactly 1 mm on a separate copy; empty parameters; resulting set is open, not a watertight enlarged solid.'
RECIPE_TOOLS[0]['inputSchema']['properties']['recipeId']['enum'].append('slice_test_cube_xy')
RECIPE_TOOLS[0]['inputSchema']['properties']['parameters']['oneOf'].append({'type':'object','properties':{'firstZMm':{'type':'number','minimum':0,'maximum':19.9},'lastZMm':{'type':'number','minimum':0,'maximum':19.9},'stepMm':{'type':'number','minimum':0.1,'maximum':20}},'required':['firstZMm','lastZMm','stepMm'],'additionalProperties':False})
RECIPE_TOOLS[0]['description'] += ' slice_test_cube_xy plans 1..3 XY sections of the fixed baseline cube. First height is 0 or >=0.1 mm; last height<=19.9 mm; stepMm>=0.1. Six decimal places maximum, last height on exact grid. Creates one DGK per layer, verifies individual imports and reopens the combined solid/curves result; source solid unchanged. Sets principal plane XY; earlier plane not restored.'

def slice_grid(parameters):
    if set(parameters) != {'firstZMm', 'lastZMm', 'stepMm'}:
        raise ValueError('Invalid slice parameter names')
    values = [parameters[k] for k in ('firstZMm', 'lastZMm', 'stepMm')]
    if any(type(x) not in (int, float) or not math.isfinite(x) or abs(x)>20 for x in values):
        raise ValueError('Invalid slice numbers')
    micros = [Decimal(str(x))*1000000 for x in values]
    if any(x != x.to_integral_value() for x in micros):
        raise ValueError('Slice precision exceeds six decimal places')
    first, last, step = map(int, micros)
    if first<0 or 0<first<100000 or last>19900000 or last<first or step<100000 or (last-first)%step:
        raise ValueError('Invalid slice interval or step grid')
    count = 1+(last-first)//step
    if count>3:
        raise ValueError('At most three slices per operation')
    return [(first+n*step)/1000000 for n in range(count)]

def validate_mutation_args(name, args):
    if name == 'prepare_test_surface_job':
        valid = not args
    elif name == 'execute_test_surface_job_phase':
        valid = set(args)=={'jobId','planSha256','phase','index'}
        valid = valid and isinstance(args['jobId'],str) and bool(re.fullmatch('[0-9a-f]{32}',args['jobId'])) and isinstance(args['planSha256'],str) and bool(re.fullmatch('[0-9a-f]{64}',args['planSha256'])) and args['phase'] in SURFACE_PHASES and type(args['index']) is int and 0<=args['index']<=24 and (args['phase'] in ('slice','verify') or args['index']==0)
    elif name == 'prepare_recipe':
        parameters = args.get('parameters')
        valid = set(args) == {'recipeId', 'parameters'} and isinstance(parameters, dict)
        if valid and args.get('recipeId') == 'rotate_z':
            valid = set(parameters) == {'angleDegrees'} and type(parameters['angleDegrees']) is int and 0 < abs(parameters['angleDegrees']) <= 180
        elif valid and args.get('recipeId') == 'assign_test_level':
            valid = set(parameters) == {'levelNumber'} and type(parameters['levelNumber']) is int and 900 <= parameters['levelNumber'] <= 909
        elif valid and args.get('recipeId') == 'create_workplane_world':
            valid = set(parameters) == {'originWorldMm', 'zAxis', 'yAxis'}
            valid = valid and all(isinstance(parameters[k], list) and len(parameters[k]) == 3 and all(type(x) in (int, float) and math.isfinite(x) for x in parameters[k]) for k in parameters)
            if valid:
                origin, z, y = (parameters[k] for k in ('originWorldMm', 'zAxis', 'yAxis'))
                valid = all(abs(x) <= 100 for x in origin) and abs(sum(x*x for x in z)-1) <= 1e-9 and abs(sum(x*x for x in y)-1) <= 1e-9 and abs(sum(a*b for a, b in zip(z, y))) <= 1e-9
        elif valid and args.get('recipeId') in ('convert_test_solid_to_surfaces','offset_test_top_surface_1mm'):
            valid = not parameters
        elif valid and args.get('recipeId') == 'scale_uniform_world':
            factor=parameters.get('scaleFactor')
            valid=set(parameters)=={'scaleFactor'} and type(factor) in (int,float) and math.isfinite(factor) and 0.5<=factor<=2 and factor!=1
        elif valid and args.get('recipeId') == 'slice_test_cube_xy':
            try:
                slice_grid(parameters)
            except ValueError:
                valid = False
        else:
            valid = False
    elif name == 'activate_test_baseline':
        valid = not args
    elif name == 'prepare_translation':
        vector = args.get('deltaWorldMm')
        valid = set(args) == {'deltaWorldMm'} and isinstance(vector, list) and len(vector) == 3
        valid = valid and all(type(x) in (int, float) and math.isfinite(x) and abs(x) <= 100 for x in vector) and any(vector)
    else:
        valid = set(args) == {'operationId', 'planSha256'}
        valid = valid and isinstance(args['operationId'], str) and re.fullmatch('[0-9a-f]{32}', args['operationId'])
        valid = valid and isinstance(args['planSha256'], str) and re.fullmatch('[0-9a-f]{64}', args['planSha256'])
    if not valid:
        raise RpcError(-32602, 'Invalid fixed mutation tool arguments')


class RpcError(Exception):
    def __init__(self, code, message):
        self.code, self.message = code, message


from core_config import general_core


class Server:
    def __init__(self, root, timeout=30, read_only=False):
        self.root = Path(root).resolve()
        self.timeout = timeout
        self.initialized = False
        self.ready = False
        self.output_lock = threading.Lock()
        self.pending_lock = threading.Lock()
        self.pending = {}
        self.snapshot_lock = threading.Lock()
        self.reference_lock = threading.Lock()
        self.references = ReferenceStore()
        self.document_handles = DocumentHandleStore()
        self.entity_sets = EntitySetStore()
        self.operation_plans = OperationPlanStore(self.root, self.entity_sets)
        self.working_copies = WorkingCopyStore()
        self.document_bindings = DocumentBindingStore(self.root, self.entity_sets, self.working_copies)
        self.owned_operations = OwnedOperationStore(self.root, self.operation_plans, self.document_bindings)
        self.read_only = read_only
        self.general_core = False
        self.tools = list(TOOLS)
        config = self.root / 'bridge.config.json'
        if config.is_file():
            settings = json.loads(config.read_text(encoding='utf-8-sig'))
            self.general_core = general_core(settings)
            if self.general_core:
                self.tools = [TOOLS[0]] + REFERENCE_TOOLS + DOCUMENT_TOOLS + [OPERATION_RESULT_TOOL]
                if settings.get('entitySetsExperimental') is True:
                    self.tools.extend(ENTITY_SET_TOOLS)
                if settings.get('operationPlansExperimental') is True:
                    self.tools.extend(OPERATION_PLAN_TOOLS)
                if settings.get('workingCopiesExperimental') is True:
                    self.tools.extend(WORKING_COPY_TOOLS)
                if settings.get('documentBindingsExperimental') is True:
                    self.tools.extend(DOCUMENT_BINDING_TOOLS)
                if settings.get('workingCopyOpenExperimental') is True:
                    self.tools.extend(OWNED_OPEN_TOOLS)
                if settings.get('ownedOperationPlansExperimental') is True:
                    self.tools.extend(OWNED_OPERATION_TOOLS)
                if settings.get('ownedTranslationsExperimental') is True:
                    self.tools.extend(OWNED_TRANSLATION_TOOLS)
            if settings.get('diskVerificationPanel') is True:
                self.tools.append(DISK_TOOL)
            if settings.get('mcpMutationsPanel') is True:
                self.tools.extend(MUTATION_TOOLS)
                if settings.get('recipeOperationsPanel') is True:
                    self.tools.extend(RECIPE_TOOLS)
                    self.tools.extend(SURFACE_TOOLS)
                    self.tools.extend(REFERENCE_TOOLS)
                    self.tools.extend(DOCUMENT_TOOLS)
                self.tools.append(OPERATION_RESULT_TOOL)
        if read_only:
            # Set handles are issued only by preparation, absent in the passive server.
            self.tools = [tool for tool in self.tools if tool['annotations'].get('readOnlyHint') is True and tool['name'] not in {'validate_entity_set', 'inspect_operation_plan', 'inspect_working_copy', 'validate_working_document_binding', 'validate_owned_operation'}]

    def send(self, value):
        data = json.dumps(value, ensure_ascii=False, allow_nan=False, separators=(',', ':')).encode('utf-8') + b'\n'
        with self.output_lock:
            sys.stdout.buffer.write(data)
            sys.stdout.buffer.flush()

    def reply(self, request_id, result=None, error=None):
        value = {'jsonrpc': '2.0', 'id': request_id}
        value['error' if error else 'result'] = error if error else result
        self.send(value)

    def status(self):
        config = self.root / 'bridge.config.json'
        result = {'adapterVersion': ADAPTER_VERSION, 'bridgeRoot': str(self.root),
                  'configurationPresent': config.is_file(), 'panelLiveness': 'not_verified',
                  'geometryMutations': 'disabled', 'transport': 'stdio'}
        result['serverMode'] = 'read_only' if self.read_only else 'general_core' if self.general_core else 'qualified_test_and_read'
        result['configurationProfile'] = 'general-v1' if self.general_core else 'legacy_fixture'
        result['entitySets'] = 'experimental_observations_only' if any(t['name']=='inspect_entity_candidates' for t in self.tools) else 'disabled'
        result['operationPlans'] = 'experimental_local_only_execution_disabled' if any(t['name']=='prepare_operation_plan' for t in self.tools) else 'disabled'
        result['workingCopies'] = 'experimental_saved_disk_bytes_only' if any(t['name']=='prepare_working_copy' for t in self.tools) else 'disabled'
        result['documentBindings'] = 'experimental_passive_owned_active_document' if any(t['name']=='bind_working_document' for t in self.tools) else 'disabled'
        result['workingCopyOpen'] = 'experimental_owned_new_window_only' if any(t['name']=='open_working_copy' for t in self.tools) else 'disabled'
        result['ownedOperationPlans'] = 'experimental_coupled_guard_execution_disabled' if any(t['name']=='prepare_owned_operation' for t in self.tools) else 'disabled'
        result['ownedTranslations'] = 'experimental_one_owned_solid_independent_verification' if any(t['name']=='execute_owned_translation' for t in self.tools) else 'disabled'
        result['maintenance'] = (self.root / 'maintenance.json').is_file()
        for filename, key in (('deployment-current.json', 'installedDeployment'), ('panel-status.json', 'lastPanelReport')):
            report = self.root / 'reports' / filename
            if report.is_file():
                try:
                    result[key] = json.loads(report.read_text(encoding='utf-8-sig'))
                except (ValueError, OSError):
                    result[key] = None
        result['referenceScope'] = 'ephemeral_read_observations; not accepted by mutation tools'
        result['documentChoice'] = 'enabled' if any(tool['name']=='activate_document' for tool in self.tools) else 'disabled'
        if config.is_file():
            data = json.loads(config.read_text(encoding='utf-8-sig'))
            result['expectedProject'] = data.get('expectedProject')
            result['captureViewport'] = data.get('captureViewport', False)
            result['workerTimeoutSeconds'] = data.get('operationTimeoutSeconds') or 60
            result['geometryMutations'] = 'qualified_test_only' if data.get('mcpMutationsPanel') is True and not self.read_only else 'disabled'
            if any(t['name']=='execute_owned_translation' for t in self.tools):
                result['geometryMutations'] = 'experimental_owned_solid_translation_only'
        stage = self.root / 'reports' / 'worker-stage.json'
        if stage.is_file():
            try:
                result['lastWorkerStage'] = json.loads(stage.read_text(encoding='utf-8-sig'))
            except (ValueError, OSError):
                result['lastWorkerStage'] = None
        return result

    def snapshot(self, cancel, operation='model.snapshot', arguments=None):
        if operation not in ('model.snapshot', 'disk.verify', 'baseline.activate', 'translation.prepare', 'translation.execute', 'recipe.prepare', 'recipe.execute'):
            raise ValueError('Unknown fixed queue operation')
        # One request per adapter; the panel serializes all clients' requests.
        deadline = time.monotonic() + self.timeout
        while not self.snapshot_lock.acquire(timeout=0.1):
            if cancel.is_set():
                return {'state': 'cancelled_before_submission'}
            if time.monotonic() >= deadline:
                return {'state': 'queue_timeout_before_submission'}
        request = None
        try:
            if cancel.is_set():
                return {'state': 'cancelled_before_submission'}
            if (self.root / 'maintenance.json').is_file():
                return {'state': 'maintenance_before_submission', 'error': 'Runtime maintenance in progress; no request submitted.'}
            if not (self.root / 'PSBridge.Worker.exe').is_file():
                return {'state': 'failed', 'error': 'Bridge worker is missing at the configured root.'}
            folder = self.root / 'requests'
            folder.mkdir(exist_ok=True)
            token = uuid.uuid4().hex
            request = folder / (token + '.json')
            temporary = folder / (token + '.tmp')
            response = self.root / 'responses' / (token + '.json')
            envelope = {'requestId': token, 'operation': operation}
            if arguments is not None:
                envelope['arguments'] = arguments
            temporary.write_text(json.dumps(envelope), encoding='utf-8')
            if (self.root / 'maintenance.json').is_file():
                temporary.unlink(missing_ok=True)
                return {'state': 'maintenance_before_submission'}
            temporary.rename(request)
            while time.monotonic() < deadline:
                if cancel.is_set():
                    return {'state': 'cancelled_outcome_unknown', 'bridgeRequestId': token,
                            'error': 'Cancellation does not stop a claimed PowerShape operation.'}
                if response.is_file():
                    if response.stat().st_size > 16000000:
                        raise ValueError('Bridge response exceeds size limit.')
                    result = json.loads(response.read_text(encoding='utf-8-sig'))
                    if not isinstance(result, dict) or not isinstance(result.get('state'), str):
                        raise ValueError('Invalid Bridge response.')
                    result['bridgeRequestId'] = token
                    return result
                time.sleep(0.1)
            return {'state': 'timeout_outcome_unknown', 'bridgeRequestId': token,
                    'error': 'No response before deadline. Check the panel, its read-request checkbox and PowerShape dialogs. No retry performed.'}
        finally:
            # Remove only an unclaimed request. Never stop PowerShape or replay.
            if request is not None:
                try:
                    request.unlink(missing_ok=True)
                except OSError:
                    pass
            self.snapshot_lock.release()

    def image_block(self, result):
        capture = result.get('viewportCapture')
        if not capture:
            return None
        path = Path(capture['path']).resolve()
        if path.parent != (self.root / 'captures').resolve() or path.suffix.lower() != '.jpg':
            raise ValueError('Capture is outside the fixed captures directory.')
        if path.stat().st_size > 12000000:
            raise ValueError('Capture exceeds size limit.')
        data = path.read_bytes()
        if hashlib.sha256(data).hexdigest() != capture.get('sha256') or not data.startswith(b'\xff\xd8'):
            raise ValueError('Capture integrity check failed.')
        return {'type': 'image', 'mimeType': 'image/jpeg', 'data': base64.b64encode(data).decode('ascii')}

    def operation_result(self, args):
        identity = args.get('operationId')
        if set(args)!={'operationId'} or not isinstance(identity,str) or not re.fullmatch('[0-9a-f]{32}',identity):
            raise RpcError(-32602,'Invalid operation identity')
        directory = (self.root/'operations').resolve()
        if (directory / identity).is_dir():
            try:
                journal = journal_events(self.root, identity)
            except (OSError, ValueError, KeyError, TypeError) as error:
                return {'state': 'operation_journal_unavailable', 'operationId': identity, 'outcome': 'unknown', 'retryAllowed': False,
                        'error': {'code': getattr(error, 'code', 'operation_journal_unreadable'), 'message': str(error)},
                        'qualification': 'No CAD verification or command retry. Inspect original journal and independent evidence.'}
            return {'state': 'operation_receipt_read', 'operationId': identity, 'receiptSha256': journal['journalSha256'],
                    'receipt': journal, 'qualification': 'Recorded journal only; no live CAD verification or retry. An interrupted writer/claim/command is reported as outcome_unknown.'}
        path = (directory/(identity+'.json')).resolve()
        if path.parent!=directory or path.stat().st_size>1000000:
            raise ValueError('Operation receipt outside fixed directory or too large')
        data = path.read_bytes()
        receipt = json.loads(data.decode('utf-8-sig'))
        if receipt.get('operationId')!=identity or not isinstance(receipt.get('state'),str):
            raise ValueError('Receipt identity/state differs')
        return {'state':'operation_receipt_read','operationId':identity,'receiptSha256':hashlib.sha256(data).hexdigest(),'receipt':receipt,'qualification':'Recorded journal only; current model/export files and live PowerShape context not checked.'}

    def prepare_surface_job(self):
        source_id='ce7c3a8f6fb8410c822ebee8552c780a'
        receipt_path=self.root/'reports'/('exchange-'+source_id+'.json')
        data=receipt_path.read_bytes()
        if len(data)>1000000: raise ValueError('Source receipt too large')
        receipt=json.loads(data.decode('utf-8-sig'))
        source=(self.root.parent.parent/'P2_Disk_Verification'/('ExchangeImported_'+source_id+'.psmodel')).resolve()
        if receipt.get('state')!='succeeded_dgk_roundtrip' or Path(receipt['importedProjectPath']).resolve()!=source or hashlib.sha256(source.read_bytes()).hexdigest()!=receipt['importedProjectSha256']:
            raise ValueError('Qualified surface source changed')
        identity=uuid.uuid4().hex
        plan={'jobId':identity,'sourceReceiptSha256':hashlib.sha256(data).hexdigest(),'heightsWorldMm':[n*800000/1000000 for n in range(25)]}
        directory=self.root/'surfacejobs';directory.mkdir(exist_ok=True)
        path=directory/(identity+'.json')
        with path.open('x',encoding='utf-8') as stream:
            json.dump(plan,stream,allow_nan=False,indent=2);stream.flush();os.fsync(stream.fileno())
        return {'state':'surface_job_plan_ready','jobId':identity,'plan':plan,'planPath':str(path),'planSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'cadMutationAttempted':False,'scope':'Qualified cube wall surfaces only. Live context is checked by each execution phase.'}

    def call(self, request_id, params, cancel):
        try:
            name = params.get('name')
            args = params.get('arguments', {})
            if not isinstance(args, dict):
                raise RpcError(-32602, 'arguments must be an object')
            if name not in {tool['name'] for tool in self.tools}:
                raise RpcError(-32602, 'Unknown tool')
            allowed = {'includeImage'} if name == 'model_snapshot' else set()
            mutating = name in {t['name'] for t in MUTATION_TOOLS + RECIPE_TOOLS + SURFACE_TOOLS}
            if mutating:
                validate_mutation_args(name, args)
                config = json.loads((self.root / 'bridge.config.json').read_text(encoding='utf-8-sig'))
                if config.get('mcpMutationsPanel') is not True:
                    raise RpcError(-32602, 'Test operations disabled by Bridge configuration')
            elif name not in {'operation_result', 'resolve_entity_references', 'activate_document', 'prepare_entity_set', 'validate_entity_set', 'prepare_operation_plan', 'inspect_operation_plan', 'prepare_working_copy', 'inspect_working_copy', 'bind_working_document', 'validate_working_document_binding', 'open_working_copy', 'verify_working_copy_open', 'prepare_owned_operation', 'validate_owned_operation', 'execute_owned_translation', 'verify_owned_translation'} and (set(args) - allowed or ('includeImage' in args and type(args['includeImage']) is not bool)):
                raise RpcError(-32602, 'Invalid tool arguments')
            if name == 'activate_document' and (set(args)!={'documentHandle'} or not isinstance(args.get('documentHandle'),str) or not re.fullmatch('[0-9a-f]{32}',args['documentHandle'])):
                raise RpcError(-32602, 'Invalid document choice arguments')
            if name == 'prepare_entity_set':
                handles = args.get('candidateEntityHandles')
                valid = set(args) == {'candidateDocumentHandle', 'candidateEntityHandles'} and entity_set_token(args.get('candidateDocumentHandle'))
                valid = valid and isinstance(handles, list) and 1 <= len(handles) <= 128 and all(entity_set_token(x) for x in handles)
                if not valid or len(set(handles)) != len(handles):
                    raise RpcError(-32602, 'Invalid entity candidate arguments')
            if name == 'validate_entity_set' and (set(args) != {'entitySetHandle'} or not entity_set_token(args.get('entitySetHandle'))):
                raise RpcError(-32602, 'Invalid entity set handle')
            if name == 'prepare_operation_plan':
                try:
                    validate_plan_arguments(args)
                except ReferenceRejected as error:
                    raise RpcError(-32602, error.code)
            if name == 'inspect_operation_plan' and (set(args) != {'operationPlanHandle'} or not entity_set_token(args.get('operationPlanHandle'))):
                raise RpcError(-32602, 'Invalid operation plan handle')
            if name == 'prepare_working_copy':
                try:
                    validate_copy_source(args)
                except ReferenceRejected as error:
                    raise RpcError(-32602, error.code)
            if name == 'inspect_working_copy' and (set(args) != {'workingCopyHandle'} or not entity_set_token(args.get('workingCopyHandle'))):
                raise RpcError(-32602, 'Invalid working copy handle')
            if name == 'bind_working_document' and (set(args) != {'candidateDocumentHandle', 'workingCopyHandle'} or not all(entity_set_token(args.get(k)) for k in ('candidateDocumentHandle', 'workingCopyHandle'))):
                raise RpcError(-32602, 'Invalid owned document binding handles')
            if name == 'validate_working_document_binding' and (set(args) != {'workingDocumentBindingHandle'} or not entity_set_token(args.get('workingDocumentBindingHandle'))):
                raise RpcError(-32602, 'Invalid owned document binding handle')
            if name == 'open_working_copy':
                try:
                    validate_open_arguments(args)
                except ReferenceRejected as error:
                    raise RpcError(-32602, error.code)
            if name == 'verify_working_copy_open':
                try:
                    validate_verification_arguments(args)
                except ReferenceRejected as error:
                    raise RpcError(-32602, error.code)
            if name in {'prepare_owned_operation', 'validate_owned_operation'}:
                fields = {'operationPlanHandle', 'workingDocumentBindingHandle'} if name == 'prepare_owned_operation' else {'ownedOperationHandle'}
                if set(args) != fields or not all(entity_set_token(value) for value in args.values()):
                    raise RpcError(-32602, 'Invalid coupled operation handles')
            if name == 'execute_owned_translation' and (set(args) != {'ownedOperationHandle'} or not entity_set_token(args.get('ownedOperationHandle'))):
                raise RpcError(-32602, 'Invalid owned translation handle')
            if name == 'verify_owned_translation':
                try: validate_translation_verification(args)
                except ReferenceRejected as error: raise RpcError(-32602, error.code)
            if name == 'resolve_entity_references':
                tokens = args.get('entityReferences')
                valid = set(args) == {'documentReference', 'entityReferences'} and isinstance(args.get('documentReference'), str) and bool(re.fullmatch('[0-9a-f]{32}', args['documentReference']))
                valid = valid and isinstance(tokens, list) and 1 <= len(tokens) <= 128 and all(isinstance(x, str) and re.fullmatch('[0-9a-f]{32}', x) for x in tokens)
                if not valid or len(set(tokens)) != len(tokens):
                    raise RpcError(-32602, 'Invalid read reference arguments')
            if name == 'verify_saved_operation':
                config = json.loads((self.root / 'bridge.config.json').read_text(encoding='utf-8-sig'))
                if config.get('diskVerificationPanel') is not True:
                    raise RpcError(-32602, 'Disk verification disabled by Bridge configuration')
            operation = {'activate_test_baseline': 'baseline.activate', 'prepare_translation': 'translation.prepare', 'execute_translation': 'translation.execute', 'verify_saved_operation': 'disk.verify', 'prepare_recipe': 'recipe.prepare', 'execute_recipe': 'recipe.execute'}.get(name, 'model.snapshot')
            if name in {'execute_owned_translation', 'verify_owned_translation'}:
                with self.reference_lock:
                    submitted = False
                    native_request = None
                    verifying = name == 'verify_owned_translation'
                    try:
                        if cancel.is_set():
                            data = {'state': 'cancelled_before_submission', 'geometryMutationAttempted': False, 'contextChangeAttempted': False}
                        else:
                            parameters = translation_verification(args, self.entity_sets, self.document_handles) if verifying else self.owned_operations.validation_parameters(args['ownedOperationHandle'])
                            if not verifying:
                                # Consume all CAD leases before submission; disk-copy ownership leases remain available for reads/new bindings.
                                self.references.clear(); self.entity_sets.clear(); self.document_handles.clear()
                                self.operation_plans.clear(); self.document_bindings.clear(); self.owned_operations.clear()
                            submitted = True
                            data = self.snapshot(cancel, 'recipe.prepare', {'recipeId': name, 'parameters': parameters})
                            native_request = data.get('bridgeRequestId')
                            if cancel.is_set() and data.get('state') not in {'cancelled_before_submission', 'maintenance_before_submission', 'queue_timeout_before_submission'}:
                                data = {'state': 'cancelled_outcome_unknown', 'bridgeRequestId': native_request, 'geometryMutationAttempted': not verifying, 'contextChangeAttempted': False, 'cadSaveAttempted': False, 'retryAllowed': False, 'localArtifactsMayExist': True}
                            elif data.get('state') == 'owned_translation_executed_unverified':
                                translation_receipt(self.root, data, parameters)
                            elif data.get('state') == 'owned_translation_verified_observed':
                                checked_translation_verification(data, parameters)
                    except (OSError, ValueError, KeyError, TypeError) as error:
                        data = {'state': 'owned_translation_verification_rejected' if verifying else 'owned_translation_outcome_unknown' if submitted else 'owned_translation_rejected_before_change',
                                'error': {'code': getattr(error, 'code', 'owned_translation_proof_unavailable'), 'message': str(error)},
                                'geometryMutationAttempted': submitted and not verifying, 'contextChangeAttempted': False, 'cadSaveAttempted': False,
                                'retryAllowed': False, 'localArtifactsMayExist': submitted}
                        if native_request: data['bridgeRequestId'] = native_request
            elif name in {'prepare_owned_operation', 'validate_owned_operation'}:
                with self.reference_lock:
                    submitted = False
                    native_request = None
                    try:
                        preparing = name == 'prepare_owned_operation'
                        if cancel.is_set():
                            data = {'state': 'cancelled_before_submission', 'contextChangeAttempted': False, 'geometryMutationAttempted': False}
                        else:
                            parameters = self.owned_operations.prepare_parameters(args['operationPlanHandle'], args['workingDocumentBindingHandle']) if preparing else self.owned_operations.validation_parameters(args['ownedOperationHandle'])
                            submitted = True
                            data = self.snapshot(cancel, 'recipe.prepare', {'recipeId': name, 'parameters': parameters})
                            native_request = data.get('bridgeRequestId')
                            success = 'owned_operation_prepared_observed' if preparing else 'owned_operation_validated_observed'
                            if cancel.is_set() or data.get('state') != success:
                                self.owned_operations.clear()
                                if cancel.is_set() and data.get('state') not in {'cancelled_before_submission', 'maintenance_before_submission', 'queue_timeout_before_submission'}:
                                    data = {'state': 'cancelled_outcome_unknown', 'bridgeRequestId': native_request, 'contextChangeAttempted': False, 'geometryMutationAttempted': False, 'localArtifactsMayExist': preparing}
                            elif preparing:
                                data = self.owned_operations.issue(data, parameters, args['operationPlanHandle'], args['workingDocumentBindingHandle'])
                            else:
                                if self.owned_operations.validation_parameters(args['ownedOperationHandle']) != parameters:
                                    raise ReferenceRejected('owned_operation_dependency_lost_during_validation')
                                self.owned_operations.checked_validation(data, parameters)
                    except (OSError, ValueError, KeyError, TypeError) as error:
                        self.owned_operations.clear()
                        data = {'state': 'owned_operation_rejected', 'error': {'code': getattr(error, 'code', 'owned_operation_proof_unavailable'), 'message': str(error)}, 'contextChangeAttempted': False, 'geometryMutationAttempted': False, 'executionEnabled': False, 'localArtifactsMayExist': submitted}
                        if native_request: data['bridgeRequestId'] = native_request
            elif name == 'verify_working_copy_open':
                with self.reference_lock:
                    try:
                        if cancel.is_set():
                            data = {'state': 'cancelled_before_submission', 'contextChangeAttempted': False, 'geometryMutationAttempted': False}
                        else:
                            parameters = verification_parameters(args, self.entity_sets, self.document_handles)
                            data = self.snapshot(cancel, 'recipe.prepare', {'recipeId': name, 'parameters': parameters})
                            if cancel.is_set() or data.get('state') != 'owned_open_verified_observed':
                                if cancel.is_set() and data.get('state') not in {'cancelled_before_submission', 'maintenance_before_submission', 'queue_timeout_before_submission'}:
                                    data = {'state': 'cancelled_outcome_unknown', 'bridgeRequestId': data.get('bridgeRequestId'), 'contextChangeAttempted': False, 'geometryMutationAttempted': False, 'qualification': 'Journal append may be unknown; no CAD command was issued.'}
                            else:
                                if data.get('openOperationId') != args['openRequestId'] or data.get('openReceiptSha256') != args['openReceiptSha256'] or data.get('expiresUtc') != parameters['expiresUtc']:
                                    raise ReferenceRejected('verified_open_native_identity_differs')
                                data = self.working_copies.issue_verified_open(data)
                    except (OSError, ValueError, KeyError, TypeError) as error:
                        data = {'state': 'owned_open_verification_rejected', 'error': {'code': getattr(error, 'code', 'owned_open_verification_unavailable'), 'message': str(error)}, 'contextChangeAttempted': False, 'geometryMutationAttempted': False}
            elif name == 'open_working_copy':
                with self.reference_lock:
                    submitted = False
                    native_request = None
                    try:
                        if cancel.is_set():
                            data = {'state': 'cancelled_before_submission', 'contextChangeAttempted': False, 'geometryMutationAttempted': False}
                        else:
                            parameters = prepare_open_parameters(args, self.entity_sets, self.working_copies, self.document_handles)
                            self.document_handles.clear()
                            self.references.clear()
                            self.entity_sets.clear()
                            self.document_bindings.clear()
                            submitted = True
                            data = self.snapshot(cancel, 'recipe.prepare', {'recipeId': name, 'parameters': parameters})
                            native_request = data.get('bridgeRequestId')
                            if cancel.is_set() and data.get('state') not in {'cancelled_before_submission', 'queue_timeout_before_submission', 'maintenance_before_submission'}:
                                data = {'state': 'working_copy_open_outcome_unknown', 'bridgeRequestId': data.get('bridgeRequestId'), 'contextChangeAttempted': True, 'geometryMutationAttempted': False, 'retryAllowed': False}
                            if data.get('state') == 'working_copy_opened_observed':
                                verify_open_receipt(self.root, data, parameters)
                            elif data.get('state') not in {'working_copy_open_rejected', 'cancelled_before_submission', 'queue_timeout_before_submission', 'maintenance_before_submission'}:
                                self.working_copies.clear()
                    except (OSError, ValueError, KeyError, TypeError) as error:
                        if submitted:
                            self.working_copies.clear()
                        data = {'state': 'working_copy_open_outcome_unknown' if submitted else 'working_copy_open_rejected', 'error': {'code': getattr(error, 'code', 'owned_open_data_unavailable'), 'message': str(error)}, 'contextChangeAttempted': submitted, 'geometryMutationAttempted': False, 'retryAllowed': False}
                        if submitted:
                            data['bridgeRequestId'] = native_request
            elif name in {'bind_working_document', 'validate_working_document_binding'}:
                with self.reference_lock:
                    try:
                        preparing = name == 'bind_working_document'
                        parameters = self.document_bindings.prepare_parameters(args['candidateDocumentHandle'], args['workingCopyHandle']) if preparing else self.document_bindings.validation_parameters(args['workingDocumentBindingHandle'])
                        data = self.snapshot(cancel, 'recipe.prepare', {'recipeId': name, 'parameters': parameters})
                        success = 'working_document_bound_observed' if preparing else 'working_document_binding_validated_observed'
                        if cancel.is_set() or data.get('state') != success:
                            self.document_bindings.clear()
                            self.entity_sets.clear()
                            if cancel.is_set() and data.get('state') not in {'cancelled_before_submission', 'queue_timeout_before_submission', 'maintenance_before_submission'}:
                                data = {'state': 'cancelled_outcome_unknown', 'bridgeRequestId': data.get('bridgeRequestId'), 'cadMutationAttempted': False, 'contextChangeAttempted': False, 'localArtifactsMayExist': preparing}
                        elif preparing:
                            data = self.document_bindings.issue(data, parameters, args['candidateDocumentHandle'], args['workingCopyHandle'])
                        else:
                            if self.document_bindings.validation_parameters(args['workingDocumentBindingHandle']) != parameters:
                                raise ReferenceRejected('document_binding_source_identity_differs')
                            self.document_bindings.checked_validation(data, parameters)
                    except (OSError, ValueError, KeyError, TypeError) as error:
                        self.document_bindings.clear()
                        data = {'state': 'working_document_binding_rejected', 'error': {'code': getattr(error, 'code', 'document_binding_data_unavailable'), 'message': str(error)}, 'cadMutationAttempted': False, 'contextChangeAttempted': False}
            elif name in {'prepare_working_copy', 'inspect_working_copy'}:
                with self.reference_lock:
                    try:
                        parameters = args if name == 'prepare_working_copy' else self.working_copies.inspect_parameters(args['workingCopyHandle'])
                        data = self.snapshot(cancel, 'recipe.prepare', {'recipeId': name, 'parameters': parameters})
                        if cancel.is_set() or data.get('state') not in {'working_copy_prepared_disk', 'working_copy_inspected_disk'}:
                            self.working_copies.clear()
                            if cancel.is_set() and data.get('state') not in {'cancelled_before_submission', 'queue_timeout_before_submission', 'maintenance_before_submission'}:
                                data = {'state': 'cancelled_outcome_unknown', 'bridgeRequestId': data.get('bridgeRequestId'), 'cadMutationAttempted': False, 'localArtifactsMayExist': True}
                        elif name == 'prepare_working_copy':
                            data = self.working_copies.issue(data)
                        elif data.get('copyId') != parameters['copyId'] or data.get('ownershipSha256') != parameters['ownershipSha256']:
                            raise ReferenceRejected('working_copy_native_receipt_identity_differs')
                    except (OSError, ValueError, KeyError, TypeError) as error:
                        self.working_copies.clear()
                        data = {'state': 'working_copy_rejected', 'error': {'code': getattr(error, 'code', 'working_copy_data_unavailable'), 'message': str(error)}, 'cadMutationAttempted': False}
            elif name in {'prepare_operation_plan', 'inspect_operation_plan'}:
                with self.reference_lock:
                    try:
                        if cancel.is_set():
                            data = {'state': 'cancelled_before_submission', 'cadMutationAttempted': False}
                        elif (self.root / 'maintenance.json').is_file():
                            data = {'state': 'maintenance_before_submission', 'cadMutationAttempted': False}
                        else:
                            data = self.operation_plans.prepare(args) if name == 'prepare_operation_plan' else self.operation_plans.inspect(args['operationPlanHandle'])
                    except (OSError, ValueError, KeyError, TypeError) as error:
                        self.operation_plans.clear()
                        data = {'state': 'operation_plan_rejected', 'error': {'code': getattr(error, 'code', 'operation_plan_artifact_unavailable'), 'message': str(error)}, 'cadMutationAttempted': False}
                        if hasattr(error, 'operation_id'):
                            data.update(operationId=error.operation_id, planSha256=error.plan_sha, localArtifactsMayExist=True)
            elif name in {'inspect_entity_candidates', 'prepare_entity_set', 'validate_entity_set'}:
                with self.reference_lock:
                    try:
                        if name == 'inspect_entity_candidates':
                            inspection = self.snapshot(cancel, 'recipe.prepare', {'recipeId': 'inspect_active_model', 'parameters': {}})
                            if cancel.is_set() or inspection.get('state') != 'succeeded_verified':
                                self.entity_sets.clear()
                                data = {'state': 'cancelled_read', 'cadMutationAttempted': False} if cancel.is_set() else inspection
                            else:
                                receipt = self.root / 'responses' / (inspection['bridgeRequestId'] + '.json')
                                raw = receipt.read_bytes()
                                if len(raw) > 16000000:
                                    raise ValueError('Candidate inspection receipt exceeds limit')
                                recorded = json.loads(raw.decode('utf-8-sig'))
                                delivered = dict(inspection)
                                delivered.pop('bridgeRequestId', None)
                                if isinstance(recorded, dict):
                                    receipt_request = recorded.pop('bridgeRequestId', inspection['bridgeRequestId'])
                                else:
                                    raise ValueError('Candidate inspection receipt is not an object')
                                if receipt_request != inspection['bridgeRequestId'] or recorded != delivered:
                                    raise ValueError('Candidate inspection receipt differs from delivered observations')
                                data = self.entity_sets.issue_candidates(inspection, hashlib.sha256(raw).hexdigest())
                        else:
                            parameters = self.entity_sets.prepare_parameters(args['candidateDocumentHandle'], args['candidateEntityHandles']) if name == 'prepare_entity_set' else self.entity_sets.validation_parameters(args['entitySetHandle'])
                            data = self.snapshot(cancel, 'recipe.prepare', {'recipeId': name, 'parameters': parameters})
                            if cancel.is_set() or data.get('state') not in {'entity_set_prepared_observed', 'entity_set_validated_observed'}:
                                self.entity_sets.clear()
                                if cancel.is_set():
                                    data = {'state': 'cancelled_outcome_unknown', 'bridgeRequestId': data.get('bridgeRequestId'), 'cadMutationAttempted': False}
                            elif name == 'prepare_entity_set':
                                data = self.entity_sets.issue_set(data)
                    except ReferenceRejected as error:
                        self.entity_sets.clear()
                        data = {'state': 'entity_set_rejected', 'error': {'code': error.code}, 'cadMutationAttempted': False}
                    except (OSError, ValueError, KeyError) as error:
                        self.entity_sets.clear()
                        data = {'state': 'entity_set_rejected', 'error': {'code': 'entity_set_receipt_or_data_unavailable', 'message': str(error)}, 'cadMutationAttempted': False}
            elif name in {'list_documents','activate_document'}:
                with self.reference_lock:
                    try:
                        if name == 'list_documents':
                            listing = self.snapshot(cancel,'recipe.prepare',{'recipeId':'list_documents','parameters':{}})
                            if cancel.is_set():
                                self.document_handles.clear()
                                data = {'state':'cancelled_read','contextChangeAttempted':False,'geometryMutationAttempted':False}
                            elif listing.get('state') != 'documents_listed_verified':
                                self.document_handles.clear()
                                data = listing
                            else:
                                receipt = self.root/'responses'/(listing['bridgeRequestId']+'.json')
                                data = self.document_handles.issue(listing,hashlib.sha256(receipt.read_bytes()).hexdigest())
                        else:
                            parameters = self.document_handles.consume(args['documentHandle'])
                            # The lease is consumed before submission and never replayed.
                            self.document_handles.clear()
                            self.references.clear()
                            self.entity_sets.clear()
                            self.document_bindings.clear()
                            data = self.snapshot(cancel,'recipe.prepare',{'recipeId':'activate_document','parameters':parameters})
                            data['documentHandleConsumed'] = True
                    except ReferenceRejected as error:
                        data = {'state':'document_choice_rejected','error':{'code':error.code},'contextChangeAttempted':False,'geometryMutationAttempted':False}
            elif name in {'inspect_active_model', 'resolve_entity_references'}:
                with self.reference_lock:
                    inspection = None
                    try:
                        if name == 'resolve_entity_references':
                            self.references.require(args['documentReference'], args['entityReferences'])
                        inspection = self.snapshot(cancel, 'recipe.prepare', {'recipeId': 'inspect_active_model', 'parameters': {}})
                        if cancel.is_set():
                            self.references.clear()
                            self.entity_sets.clear()
                            data = {'state': 'cancelled_read', 'cadMutationAttempted': False}
                        elif inspection.get('state') != 'succeeded_verified':
                            self.references.clear()
                            self.entity_sets.clear()
                            data = inspection
                        else:
                            self.entity_sets.observe(inspection)
                            data = self.references.issue(inspection) if name == 'inspect_active_model' else self.references.resolve(args['documentReference'], args['entityReferences'], inspection)
                    except ReferenceRejected as error:
                        data = {'state': 'read_reference_rejected', 'error': {'code': error.code}, 'cadMutationAttempted': False}
                        if inspection is not None:
                            data['inspectionRequestId'] = inspection.get('bridgeRequestId')
                            data['observedDocument'] = inspection.get('model')
            elif name=='prepare_test_surface_job':
                data=self.prepare_surface_job()
            elif name=='execute_test_surface_job_phase':
                data=self.snapshot(cancel,'recipe.prepare',{'recipeId':'test_surface_section_phase','parameters':args})
            else:
                data = self.status() if name == 'bridge_status' else self.operation_result(args) if name == 'operation_result' else self.snapshot(cancel, operation, args if mutating else None)
            content = [{'type': 'text', 'text': json.dumps(data, ensure_ascii=False, allow_nan=False)}]
            if args.get('includeImage') and data.get('state') == 'succeeded_verified':
                try:
                    block = self.image_block(data)
                    if block:
                        content.append(block)
                except (OSError, ValueError, KeyError) as ex:
                    data.setdefault('warnings', []).append('MCP image delivery: ' + str(ex))
                    content[0]['text'] = json.dumps(data, ensure_ascii=False, allow_nan=False)
            expected = {'list_documents':{'documents_listed_verified'},'activate_document':{'document_active_verified'},'inspect_active_model': {'active_model_inspected'}, 'resolve_entity_references': {'entity_references_resolved_observed'}, 'prepare_test_surface_job':{'surface_job_plan_ready'},'execute_test_surface_job_phase':{'succeeded_surface_section_phase'},'verify_saved_operation': {'succeeded_disk_verified'}, 'activate_test_baseline': {'baseline_active'}, 'prepare_translation': {'plan_ready'}, 'execute_translation': {'succeeded_saved'}, 'prepare_recipe': {'plan_ready'}, 'execute_recipe': {'succeeded_saved','succeeded_slices_saved_reopened_dgk_verified'}, 'operation_result': {'operation_receipt_read'}}.get(name, {'succeeded_verified'})
            expected = {'inspect_entity_candidates': {'entity_candidates_inspected'}, 'prepare_entity_set': {'entity_set_prepared_observed'}, 'validate_entity_set': {'entity_set_validated_observed'}}.get(name, expected)
            expected = {'prepare_operation_plan': {'operation_plan_prepared_local'}, 'inspect_operation_plan': {'operation_plan_inspected_local'}}.get(name, expected)
            expected = {'prepare_working_copy': {'working_copy_prepared_disk'}, 'inspect_working_copy': {'working_copy_inspected_disk'}}.get(name, expected)
            expected = {'bind_working_document': {'working_document_bound_observed'}, 'validate_working_document_binding': {'working_document_binding_validated_observed'}}.get(name, expected)
            expected = {'open_working_copy': {'working_copy_opened_observed'}}.get(name, expected)
            expected = {'verify_working_copy_open': {'owned_open_verified_observed'}}.get(name, expected)
            expected = {'execute_owned_translation': {'owned_translation_executed_unverified'}, 'verify_owned_translation': {'owned_translation_verified_observed'}}.get(name, expected)
            expected = {'prepare_owned_operation': {'owned_operation_prepared_observed'}, 'validate_owned_operation': {'owned_operation_validated_observed'}}.get(name, expected)
            result = {'content': content, 'structuredContent': data,
                      'isError': name != 'bridge_status' and data.get('state') not in expected}
            self.reply(request_id, result)
        except RpcError as ex:
            self.reply(request_id, error={'code': ex.code, 'message': ex.message})
        except Exception as ex:
            self.reply(request_id, {'content': [{'type': 'text', 'text': str(ex)}], 'isError': True})
        finally:
            with self.pending_lock:
                self.pending.pop(request_id, None)

    def dispatch(self, message):
        if not isinstance(message, dict) or message.get('jsonrpc') != '2.0' or not isinstance(message.get('method'), str):
            raise RpcError(-32600, 'Invalid request')
        method, params = message['method'], message.get('params', {})
        request_id = message.get('id')
        if not isinstance(params, dict):
            raise RpcError(-32602, 'params must be an object')
        if 'id' not in message:
            if method == 'notifications/initialized' and self.initialized:
                self.ready = True
            elif method == 'notifications/cancelled':
                target = params.get('requestId')
                if type(target) in (str, int):
                    with self.pending_lock:
                        event = self.pending.get(target)
                        if event:
                            event.set()
            return
        if type(request_id) not in (str, int):
            raise RpcError(-32600, 'Request id must be a string or integer')
        if method == 'initialize':
            if self.initialized:
                raise RpcError(-32600, 'Already initialized')
            if not isinstance(params.get('protocolVersion'), str) or not isinstance(params.get('capabilities'), dict) or not isinstance(params.get('clientInfo'), dict):
                raise RpcError(-32602, 'Invalid initialize parameters')
            version = params['protocolVersion']
            self.initialized = True
            self.reply(request_id, {'protocolVersion': version if version in VERSIONS else VERSIONS[0],
                       'capabilities': {'tools': {'listChanged': False}},
                       'serverInfo': {'name': 'powershape-bridge', 'version': ADAPTER_VERSION},
                       'instructions': ('General core provides reads and already-open document choice; no general geometry editing tools are accepted yet. Experimental observed entity sets, when enabled, do not authorize an edit. ' if self.general_core else 'Geometry editing tools support only the qualified test baseline. ') + 'Keep the user-session panel open. Document activation changes context only; read-only mode excludes activation and geometry tools. Never retry an unknown outcome automatically. Native IDs are not persistent; measurements do not certify complete topology.'})
        elif method == 'ping':
            self.reply(request_id, {})
        elif not self.ready:
            raise RpcError(-32600, 'Complete initialization first')
        elif method == 'tools/list':
            self.reply(request_id, {'tools': self.tools})
        elif method == 'tools/call':
            with self.pending_lock:
                if request_id in self.pending or len(self.pending) >= 8:
                    raise RpcError(-32600, 'Duplicate request id or too many pending calls')
                cancel = threading.Event()
                self.pending[request_id] = cancel
            threading.Thread(target=self.call, args=(request_id, params, cancel), daemon=True).start()
        else:
            raise RpcError(-32601, 'Method not found')

    def run(self):
        while True:
            line = sys.stdin.buffer.readline(MAX_MESSAGE + 1)
            if not line:
                break
            request_id = None
            try:
                if len(line) > MAX_MESSAGE:
                    while line and not line.endswith(b'\n'):
                        line = sys.stdin.buffer.readline(MAX_MESSAGE + 1)
                    raise RpcError(-32600, 'Message too large')
                try:
                    value = json.loads(line.decode('utf-8'), parse_constant=lambda _: (_ for _ in ()).throw(ValueError('Non-finite number')))
                except (ValueError, UnicodeError):
                    raise RpcError(-32700, 'Parse error')
                if isinstance(value, dict) and type(value.get('id')) in (str, int):
                    request_id = value['id']
                self.dispatch(value)
            except RpcError as ex:
                self.reply(request_id, error={'code': ex.code, 'message': ex.message})
        with self.pending_lock:
            for event in self.pending.values():
                event.set()


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--bridge-root', required=True)
    parser.add_argument('--timeout', type=float, default=30)
    parser.add_argument('--read-only', action='store_true', help='Publish only tools marked read-only; no mutation tools or image capture.')
    options = parser.parse_args()
    if not 1 <= options.timeout <= 120:
        parser.error('timeout must be 1..120 seconds')
    Server(options.bridge_root, options.timeout, options.read_only).run()
