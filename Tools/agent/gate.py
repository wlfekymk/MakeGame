import re,sys
bad=0
for f in sys.argv[1:]:
    s=open(f,encoding='utf-8').read()
    out=[];i=0;n=len(s)
    while i<n:
        c=s[i]
        if c=='@' and i+1<n and s[i+1]=='"':
            i+=2
            while i<n:
                if s[i]=='"':
                    if i+1<n and s[i+1]=='"': i+=2; continue
                    i+=1; break
                i+=1
            out.append(' ');continue
        if c=='"':
            i+=1
            while i<n and s[i]!='"':
                if s[i]=='\\': i+=1
                i+=1
            i+=1; out.append(' ');continue
        if c=="'":
            i+=1
            while i<n and s[i]!="'":
                if s[i]=='\\': i+=1
                i+=1
            i+=1; out.append(' ');continue
        if c=='/' and i+1<n and s[i+1]=='/':
            while i<n and s[i]!='\n': i+=1
            continue
        if c=='/' and i+1<n and s[i+1]=='*':
            i+=2
            while i+1<n and not(s[i]=='*' and s[i+1]=='/'): i+=1
            i+=2; out.append(' ');continue
        out.append(c);i+=1
    t=''.join(out)
    for op,cl,name in (('{','}','brace'),('(',')','paren'),('[',']','bracket')):
        d=t.count(op)-t.count(cl)
        if d: print(f"FAIL {f}: {name} {d}"); bad=1
    ifs=len(re.findall(r'(?m)^\s*#if',s)); ends=len(re.findall(r'(?m)^\s*#endif',s))
    if ifs!=ends: print(f"FAIL {f}: #if {ifs} != #endif {ends}"); bad=1
    if re.search(r'\bvoid\s+OnGUI\s*\(',t): print(f"FAIL {f}: OnGUI"); bad=1
print("STATIC GATE:", "FAIL" if bad else "PASS")
