import datetime,os
import urllib.request,urllib.error,http.cookiejar,json
jar=http.cookiejar.CookieJar();c=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar));base=os.environ.get('APPROVALFLOW_BASE_URL','http://localhost:8790');started=datetime.datetime.now(datetime.timezone.utc).isoformat()
token=json.load(c.open(base+'/api/csrf'))['token'];statuses=[]
for i in range(11):
 r=urllib.request.Request(base+'/api/login',data=json.dumps({'email':'missing@example.test','password':'incorrect'}).encode(),headers={'Content-Type':'application/json','X-CSRF-TOKEN':token})
 try:
  with c.open(r) as response:statuses.append(response.status)
 except urllib.error.HTTPError as e:statuses.append(e.code)
assert statuses == [401] * 10 + [429], statuses
print(json.dumps({'run_id':os.environ.get('VERIFY_RUN_ID',started),'started_at':started,'ended_at':datetime.datetime.now(datetime.timezone.utc).isoformat(),'base_url':base,'test':'default login limit: 10 attempts per minute','statuses':statuses,'passed':True},indent=2))
