"""Build a fresh bundle from pinned official packages and a scrubbed domooc overlay.

Never imports or executes third-party tool code, reads local tool installs, or copies
account state. Requires Python 3 and 7-Zip; the CI runner supplies both.
"""
from pathlib import Path
import configparser
import hashlib
import json
import os
import shutil
import subprocess
import sys
import urllib.request
import zipfile

REPO=Path(__file__).resolve().parents[1]
VERSION='v0.1.2'
ASSETS={
 'autovisor.zip':('https://github.com/CXRunfree/Autovisor/releases/download/3.18.4/Autovisor-3.18.4-windows-amd64.zip','11cea9e9ac1d9f122041cd82c7e6befcc891487396f16e82386f06cf2f8ea32d'),
 'unipus.7z':('https://github.com/Zzj-klwgxdz/UnipusAI/releases/download/3.6/UnipusAI-v3.6-windows-x86-64.7z','8d63bbd34d1c65be7a2bf60bf1c02570ea5d280c90209668dfa37f2494d8ed43'),
 'xuexitong.zip':('https://github.com/Mortal004/Xuexitong_shuake/releases/download/%E5%AD%A6%E4%B9%A0%E9%80%9A%E5%88%B7%E8%AF%BE%EF%BC%8C%E5%8F%AF%E6%89%A7%E8%A1%8C%E6%96%87%E4%BB%B6/xuexitong_shuake26.10.25','25e55ffdccb502a317a9a689a982736edb6205137f12cbf6f9b4d50841da33d7'),
 'firefox.exe':('https://archive.mozilla.org/pub/firefox/releases/158.0.1/win64/zh-CN/Firefox%20Setup%20158.0.1.exe','b6b75a7cc12adf3c22b93d4f2691e2784024bdb083393df5acce89ae84dee0f6'),
}
SOURCES={
 'Autovisor-3.18.4-source.zip':'https://api.github.com/repos/CXRunfree/Autovisor/zipball/3.18.4',
 'UnipusAI-3.6-source.zip':'https://api.github.com/repos/Zzj-klwgxdz/UnipusAI/zipball/3.6',
 'Xuexitong-source.zip':'https://api.github.com/repos/Mortal004/Xuexitong_shuake/zipball/%E5%AD%A6%E4%B9%A0%E9%80%9A%E5%88%B7%E8%AF%BE%EF%BC%8C%E5%8F%AF%E6%89%A7%E8%A1%8C%E6%96%87%E4%BB%B6',
}

def digest(path):
    h=hashlib.sha256()
    with path.open('rb') as stream:
        while chunk:=stream.read(1024*1024): h.update(chunk)
    return h.hexdigest()

def download(url,path,expected=None):
    request=urllib.request.Request(url,headers={'User-Agent':'Course-Boost-Script-Pack'})
    with urllib.request.urlopen(request,timeout=120) as response,path.open('xb') as output:
        shutil.copyfileobj(response,output)
    actual=digest(path)
    if expected and actual!=expected: raise ValueError('Checksum mismatch: '+path.name)
    return actual

def unzip(archive,target):
    target.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(archive) as source:
        for entry in source.infolist():
            relative=entry.filename.replace('\\','/')
            destination=(target/relative).resolve()
            if not destination.is_relative_to(target.resolve()): raise ValueError('Archive path escape')
            if entry.is_dir(): destination.mkdir(parents=True,exist_ok=True)
            else:
                destination.parent.mkdir(parents=True,exist_ok=True)
                with source.open(entry) as reader,destination.open('wb') as writer: shutil.copyfileobj(reader,writer)

def remove_generated(path,bundle):
    if not path.resolve().is_relative_to(bundle.resolve()): raise ValueError('Cleanup path escape')
    if path.is_dir(): shutil.rmtree(path)
    elif path.exists(): path.unlink()

def main():
    output=Path(sys.argv[1] if len(sys.argv)>1 else REPO/'dist').resolve()
    output.mkdir(parents=True,exist_ok=True)
    work=output/'full-pack-work'
    work.mkdir(exist_ok=False)
    bundle=work/'Course-Boost-Script-Pack'
    bundle.mkdir()
    downloads=work/'downloads'
    downloads.mkdir()
    provenance={}
    seven=shutil.which('7z') or r'C:\7-Zip\7z.exe'
    for name,(url,expected) in ASSETS.items():
        print('Downloading '+name,flush=True)
        provenance[name]={'url':url,'sha256':download(url,downloads/name,expected)}
    auto_temp=work/'autovisor'
    unzip(downloads/'autovisor.zip',auto_temp)
    auto_exe=next(auto_temp.rglob('Autovisor.exe'))
    auto=bundle/'Autovisor-3.18.4-windows-amd64'
    shutil.copytree(auto_exe.parent,auto)
    for name in ('logs','data'):
        data=auto/name
        mirror=(data/'mirrors.json').read_bytes() if name=='data' and (data/'mirrors.json').exists() else None
        remove_generated(data,bundle)
        if mirror is not None:
            data.mkdir()
            (data/'mirrors.json').write_bytes(mirror)
    template=auto/'config.ini'
    if not template.exists(): template=auto/'config.ini.example'
    config=configparser.ConfigParser(interpolation=None)
    config.optionxform=str
    config.read_string(template.read_text(encoding='utf-8-sig'))
    for section in config.sections():
        for key in list(config[section]):
            if key.lower() in ('username','password','exe_path') or (section=='course-url' and key.upper().startswith('URL')):
                config[section][key]=''
    with (auto/'config.ini').open('w',encoding='utf-8') as stream: config.write(stream)
    uni=bundle/'UnipusAI-v3.6-windows-x86-64'
    uni.mkdir()
    subprocess.run([seven,'e',str(downloads/'unipus.7z'),'UnipusAI.exe','config.example.json','-o'+str(uni),'-y'],check=True,stdout=subprocess.DEVNULL)
    settings=json.loads((uni/'config.example.json').read_text(encoding='utf-8-sig'))
    for key in list(settings):
        if key.lower() in ('username','password','cookie','authorization','refresh_token','api_key','x_annotator_auth_token','annotator_key','open_id','course_id','class_id','curricula_id'):
            settings[key]=''
    for name in ('config.json','config.example.json'): (uni/name).write_text(json.dumps(settings,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    for name in ('Import-Missing-Units.cmd','U校园使用说明.md'):
        shutil.copy2(REPO/'docs/unipus'/name,uni/name)
    xue=bundle/'xuexitong_shuake26.10'
    unzip(downloads/'xuexitong.zip',xue)
    account=next(xue.rglob('account_info.json'))
    settings=json.loads(account.read_text(encoding='utf-8-sig'))
    for key in ('phone_number','password','API','driver_path','timer_start','timer_end'): settings[key]=''
    for key in ('cour','file_name'): settings[key]=[]
    settings['timer_active']=0
    account.write_text(json.dumps(settings,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    for filename in ('course_name.json','course_process.json','face_url.json'):
        path=account.parent/filename
        if path.exists():
            existing=json.loads(path.read_text(encoding='utf-8-sig'))
            path.write_text('[]\n' if isinstance(existing,list) else '{}\n',encoding='utf-8')
    for record in list(xue.rglob('record')): remove_generated(record,bundle)
    overlay=REPO/'packaging/domooc-overlay.zip'
    if digest(overlay)!='3dd5a7822db45a5c174e394089688e7229641eeebf2fa11c6ec01c156d5485e8': raise ValueError('Overlay changed')
    unzip(overlay,bundle)
    browser_temp=work/'firefox'
    browser_temp.mkdir()
    subprocess.run([seven,'x',str(downloads/'firefox.exe'),'core/*','-o'+str(browser_temp),'-y'],check=True,stdout=subprocess.DEVNULL)
    (bundle/'domooc_free/App').mkdir()
    shutil.move(str(browser_temp/'core'),str(bundle/'domooc_free/App/Firefox64'))
    source_dir=bundle/'third-party-sources'
    source_dir.mkdir()
    license_dir=bundle/'THIRD-PARTY-LICENSES'
    license_dir.mkdir()
    for name,url in SOURCES.items():
        provenance[name]={'url':url,'sha256':download(url,source_dir/name)}
        with zipfile.ZipFile(source_dir/name) as source:
            matches=[n for n in source.namelist() if n.rsplit('/',1)[-1].lower() in ('license','license.txt','license.md','copying')]
            if not matches and name=='Xuexitong-source.zip':
                shutil.copy2(REPO/'packaging/licenses/Xuexitong-LICENSE.txt',license_dir/'Xuexitong-LICENSE.txt')
            elif not matches: raise ValueError('Missing upstream license: '+name)
            for i,entry in enumerate(matches):
                (license_dir/(name.removesuffix('-source.zip')+'-'+str(i)+'.txt')).write_bytes(source.read(entry))
    shutil.copy2(REPO/'TimeSaver.exe',bundle/'TimeSaver.exe')
    shutil.copy2(REPO/'README.md',bundle/'README.md')
    shutil.copytree(REPO/'docs',bundle/'docs')
    (bundle/'launcher').mkdir(exist_ok=True)
    (bundle/'PACK-CONTENTS.json').write_text(json.dumps(provenance,ensure_ascii=False,indent=2),encoding='utf-8')
    forbidden={'cookies.sqlite','places.sqlite','key4.db','logins.db','logins.json','sessionstore.jsonlz4'}
    for file in bundle.rglob('*'):
        if file.is_file() and (file.name in forbidden or file.suffix.lower()=='.log'):
            remove_generated(file,bundle)
    if list((bundle/'domooc_free/Profile/storage/default').glob('http*')): raise ValueError('Website storage included')
    if json.loads((uni/'config.json').read_text(encoding='utf-8'))['api_key']: raise ValueError('Unipus key not empty')
    for key in ('phone_number','password','API'):
        if json.loads(account.read_text(encoding='utf-8'))[key]: raise ValueError('Xuexitong credential not empty')
    for filename in ('Autovisor-3.18.4-windows-amd64/Autovisor.exe','UnipusAI-v3.6-windows-x86-64/UnipusAI.exe','domooc_free/App/Firefox64/firefox.exe','xuexitong_shuake26.10/exe)/start.exe','xuexitong_shuake26.10/exe)/task/main.exe'):
        if not (bundle/filename).is_file(): raise ValueError('Missing entry: '+filename)
    name='Course-Boost-Script-Pack-'+VERSION+'-windows-x64-full.zip'
    archive=output/name
    with zipfile.ZipFile(archive,'x',zipfile.ZIP_DEFLATED,compresslevel=6) as target:
        for file in sorted(bundle.rglob('*')):
            if file.is_file(): target.write(file,file.relative_to(bundle).as_posix())
    checksum=output/'SHA256SUMS-full.txt'
    checksum.write_text(digest(archive)+'  '+name+'\n',encoding='ascii')
    print(json.dumps({'zip':str(archive),'bytes':archive.stat().st_size,'sha256':digest(archive),'files':len([p for p in bundle.rglob('*') if p.is_file()])}),flush=True)

if __name__=='__main__': main()
