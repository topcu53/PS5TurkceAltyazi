# Resmi Marian parçalayıcısının kimlikleri (C# parçalayıcısıyla kıyas için)
import sys
from transformers import MarianTokenizer
tok = MarianTokenizer.from_pretrained(sys.argv[1])
samples = [l.rstrip("\n") for l in open(sys.argv[2], encoding="utf-8")]
cs = [l.split() for l in open(sys.argv[3], encoding="utf-8")]
bad = 0
for s, c in zip(samples, cs):
    ref = [str(i) for i in tok(s)["input_ids"]]
    ok = ref == c
    bad += not ok
    print(("OK  " if ok else "FARK"), s, "\n   ref:", " ".join(ref), "\n   cs: ", " ".join(c))
print("parçalayıcı farkı:", bad)
