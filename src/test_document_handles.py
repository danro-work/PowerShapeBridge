"""Context-choice lease and public route tests; no CAD is contacted."""
import copy
import datetime
import hashlib
import json
from pathlib import Path
import threading
import unittest
import uuid

from document_handles import DocumentHandleStore
from entity_references import ReferenceRejected
from mcp_server import Server


def listing():
    return {'state':'documents_listed_verified','bridgeRequestId':'a'*32,
            'timestampUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
            'processId':100,'processStartedUtc':'2026-10-09T08:00:00Z','windowsSessionId':1,
            'documents':[{'modelId':9,'name':'Part','path':'D:/Part.psmodel',
                          'isActive':True,'activationSupported':True,'windowNumber':1,'windowNumbers':[1]}]}


class Handles(unittest.TestCase):
    def setUp(self):
        self.time = 100.0
        self.store = DocumentHandleStore(clock=lambda:self.time)
        self.data = listing()

    def test_one_use_and_expiry(self):
        issued = self.store.issue(self.data,'b'*64)
        token = issued['documents'][0]['documentHandle']
        args = self.store.consume(token)
        self.assertEqual(args['listingRequestId'],'a'*32)
        self.assertEqual(args['windowNumber'],1)
        with self.assertRaises(ReferenceRejected): self.store.consume(token)
        issued = self.store.issue(self.data,'b'*64)
        self.time=401
        with self.assertRaises(ReferenceRejected): self.store.consume(issued['documents'][0]['documentHandle'])

    def test_duplicate_or_invalid_native_id_rejected(self):
        for document in ({**self.data['documents'][0],'modelId':True},
                         {**self.data['documents'][0],'windowNumber':'1'}):
            with self.assertRaises(ReferenceRejected): self.store.issue({**self.data,'documents':[document]},'b'*64)
        with self.assertRaises(ReferenceRejected): self.store.issue({**self.data,'documents':self.data['documents']*2},'b'*64)

    def test_multiple_window_document_has_no_choice_handle(self):
        self.data['documents'][0].update(activationSupported=False,windowNumber=None,windowNumbers=[1,2])
        self.assertIsNone(self.store.issue(self.data,'b'*64)['documents'][0]['documentHandle'])
        self.assertFalse(self.store.entries)

    def test_different_connection_never_accepts_token(self):
        token=self.store.issue(self.data,'b'*64)['documents'][0]['documentHandle']
        with self.assertRaises(ReferenceRejected): DocumentHandleStore().consume(token)


class Routes(unittest.TestCase):
    def setUp(self):
        self.root=Path(__file__).parents[1]/'work'/('document-test-'+uuid.uuid4().hex)
        self.root.mkdir(parents=True)
        (self.root/'responses').mkdir()
        (self.root/'bridge.config.json').write_text(json.dumps({'mcpMutationsPanel':True,'recipeOperationsPanel':True}))
        self.server=Server(self.root)
        self.replies=[]
        self.calls=[]
        self.outcome='document_active_verified'
        self.server.reply=lambda request_id,result=None,error=None:self.replies.append((result,error))
        def fake(cancel,operation,args):
            self.calls.append((operation,args))
            if args['recipeId']=='list_documents':
                data=listing()
                raw=copy.deepcopy(data)
                raw.pop('bridgeRequestId')
                (self.root/'responses'/('a'*32+'.json')).write_text(json.dumps(raw))
                return data
            return {'state':self.outcome,'geometryMutationAttempted':False}
        self.server.snapshot=fake

    def call(self,name,args=None,server=None):
        (server or self.server).call(1,{'name':name,'arguments':args or {}},threading.Event())
        return self.replies[-1]

    def test_list_activation_fixed_envelope_and_reference_revocation(self):
        data=self.call('list_documents')[0]['structuredContent']
        token=data['documents'][0]['documentHandle']
        self.server.references.documents['old']={'expires':999999999}
        result,error=self.call('activate_document',{'documentHandle':token})
        self.assertIsNone(error)
        self.assertFalse(result['isError'])
        self.assertTrue(result['structuredContent']['documentHandleConsumed'])
        self.assertFalse(self.server.references.documents)
        self.assertEqual(self.calls[-1][1]['recipeId'],'activate_document')
        self.assertEqual(self.calls[-1][1]['parameters']['listingSha256'],hashlib.sha256((self.root/'responses'/('a'*32+'.json')).read_bytes()).hexdigest())
        count=len(self.calls)
        rejected,_=self.call('activate_document',{'documentHandle':token})
        self.assertTrue(rejected['isError'])
        self.assertEqual(len(self.calls),count)

    def test_timeout_consumes_handle_without_retry(self):
        data=self.call('list_documents')[0]['structuredContent']
        token=data['documents'][0]['documentHandle']
        self.outcome='timeout_outcome_unknown'
        result,_=self.call('activate_document',{'documentHandle':token})
        self.assertTrue(result['isError'])
        count=len(self.calls)
        self.call('activate_document',{'documentHandle':token})
        self.assertEqual(len(self.calls),count)

    def test_arbitrary_native_window_or_path_rejected_before_queue(self):
        for args in ({'documentHandle':'a'*32,'windowNumber':1},{'path':'D:/Part.psmodel'},
                     {'documentHandle':'../bad'},{'documentHandle':True}):
            _,error=self.call('activate_document',args)
            self.assertEqual(error['code'],-32602)
        self.assertFalse(self.calls)

    def test_readonly_catalog_contains_list_but_no_activation(self):
        readonly=Server(self.root,read_only=True)
        readonly.reply=self.server.reply
        readonly.snapshot=self.server.snapshot
        self.assertIn('list_documents',{t['name'] for t in readonly.tools})
        self.assertNotIn('activate_document',{t['name'] for t in readonly.tools})
        _,error=self.call('activate_document',{'documentHandle':'a'*32},readonly)
        self.assertEqual(error['code'],-32602)
        self.assertFalse(self.calls)


if __name__=='__main__': unittest.main()
