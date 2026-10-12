"""One-use context-choice handles, isolated from geometry read references."""
import copy
import datetime
import re
import time
import uuid

from entity_references import ReferenceRejected


class DocumentHandleStore:
    TTL_SECONDS = 300
    MAX_HANDLES = 1024

    def __init__(self, clock=time.monotonic):
        self.clock = clock
        self.entries = {}

    def clear(self):
        self.entries.clear()

    def purge(self):
        self.entries = {k:v for k,v in self.entries.items() if v['expires']>self.clock()}

    def issue(self, listing, response_sha256):
        if listing.get('state') != 'documents_listed_verified':
            self.clear()
            raise ReferenceRejected('document_listing_failed')
        request = listing.get('bridgeRequestId')
        if not isinstance(request,str) or not re.fullmatch('[0-9a-f]{32}',request) or not re.fullmatch('[0-9a-f]{64}',response_sha256):
            self.clear()
            raise ReferenceRejected('document_listing_receipt_invalid')
        documents = listing.get('documents')
        if not isinstance(documents,list) or len(documents)>256:
            self.clear()
            raise ReferenceRejected('document_inventory_invalid')
        ids = set()
        for document in documents:
            model = document.get('modelId')
            if type(model) is not int or model<0 or model in ids:
                self.clear()
                raise ReferenceRejected('document_inventory_ambiguous')
            ids.add(model)
            if document.get('activationSupported') is True:
                number = document.get('windowNumber')
                if type(number) is not int or not 1<=number<=4096:
                    self.clear()
                    raise ReferenceRejected('document_inventory_invalid')
        self.purge()
        if len(self.entries)+len(documents)>self.MAX_HANDLES:
            self.clear()
        listed = datetime.datetime.fromisoformat(listing['timestampUtc'].replace('Z','+00:00'))
        if listed.tzinfo is None:
            raise ReferenceRejected('document_listing_time_invalid')
        expiry = listed+datetime.timedelta(seconds=self.TTL_SECONDS)
        remaining = (expiry-datetime.datetime.now(datetime.timezone.utc)).total_seconds()
        if remaining<=0 or remaining>self.TTL_SECONDS+5:
            self.clear()
            raise ReferenceRejected('document_listing_expired')
        result = copy.deepcopy(listing)
        result.update(documentHandleLifetimeSeconds=min(self.TTL_SECONDS,int(remaining)),
                      documentHandleQualification='One-use choice of an already-open document in this connection. Native window IDs/numbers are not persistent; full listing is rechecked before activation.')
        for document in result['documents']:
            if document.get('activationSupported') is not True:
                document['documentHandle']=None
                continue
            token = uuid.uuid4().hex
            self.entries[token] = {'expires':self.clock()+remaining,'parameters':{
                'listingRequestId':request,'listingSha256':response_sha256,
                'windowNumber':document['windowNumber'],'modelId':document['modelId'],
                'expiresUtc':expiry.isoformat()}}
            document['documentHandle'] = token
        return result

    def consume(self, token):
        self.purge()
        entry = self.entries.pop(token,None)
        if entry is None:
            raise ReferenceRejected('document_handle_unknown_expired_or_consumed')
        return copy.deepcopy(entry['parameters'])
