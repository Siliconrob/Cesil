``` ini

BenchmarkDotNet=v0.12.0, OS=Windows 8.1 (6.3.9600.0)
Intel Core i7-4960HQ CPU 2.60GHz (Haswell), 1 CPU, 8 logical and 4 physical cores
Frequency=2533206 Hz, Resolution=394.7567 ns, Timer=TSC
.NET Core SDK=3.0.100
  [Host]     : .NET Core 3.0.0 (CoreCLR 4.700.19.46205, CoreFX 4.700.19.46214), X64 RyuJIT
  DefaultJob : .NET Core 3.0.0 (CoreCLR 4.700.19.46205, CoreFX 4.700.19.46214), X64 RyuJIT


```
|        Method |      Class |       NameSet |            Mean |         Error |        StdDev |          Median |  Gen 0 | Gen 1 | Gen 2 | Allocated |
|-------------- |----------- |-------------- |----------------:|--------------:|--------------:|----------------:|-------:|------:|------:|----------:|
|        **Create** |      **Array** | **CommonEnglish** |       **111.50 ns** |      **2.991 ns** |      **7.932 ns** |       **107.67 ns** | **0.0138** |     **-** |     **-** |     **928 B** |
| LookupSuccess |      Array | CommonEnglish | 2,616,538.90 ns | 13,347.229 ns | 12,485.006 ns | 2,617,259.94 ns |      - |     - |     - |      19 B |
| LookupFailure |      Array | CommonEnglish | 5,045,483.39 ns | 53,068.727 ns | 44,314.791 ns | 5,025,298.85 ns |      - |     - |     - |      38 B |
|        **Create** |      **Array** |     **NarrowRow** |        **23.44 ns** |      **0.499 ns** |      **0.731 ns** |        **23.68 ns** | **0.0005** |     **-** |     **-** |      **32 B** |
| LookupSuccess |      Array |     NarrowRow |       540.21 ns |     10.613 ns |     13.422 ns |       534.03 ns |      - |     - |     - |         - |
| LookupFailure |      Array |     NarrowRow |       637.25 ns |     12.750 ns |     33.364 ns |       633.78 ns |      - |     - |     - |         - |
|        **Create** |      **Array** |       **WideRow** |        **50.77 ns** |      **0.796 ns** |      **0.745 ns** |        **50.55 ns** | **0.0049** |     **-** |     **-** |     **328 B** |
| LookupSuccess |      Array |       WideRow |   282,800.84 ns |  5,348.476 ns |  5,002.968 ns |   284,259.70 ns |      - |     - |     - |         - |
| LookupFailure |      Array |       WideRow |   523,551.50 ns |  3,108.303 ns |  2,755.430 ns |   522,631.83 ns |      - |     - |     - |       5 B |
|        **Create** | **Dictionary** | **CommonEnglish** |     **4,402.36 ns** |     **27.248 ns** |     **25.488 ns** |     **4,397.87 ns** | **0.1450** |     **-** |     **-** |   **10184 B** |
| LookupSuccess | Dictionary | CommonEnglish |   266,409.08 ns |  4,437.393 ns |  4,150.740 ns |   267,111.88 ns |      - |     - |     - |       2 B |
| LookupFailure | Dictionary | CommonEnglish |   190,052.50 ns |  4,927.218 ns |  5,476.590 ns |   187,575.35 ns |      - |     - |     - |         - |
|        **Create** | **Dictionary** |     **NarrowRow** |       **119.97 ns** |      **1.939 ns** |      **1.814 ns** |       **120.35 ns** | **0.0031** |     **-** |     **-** |     **208 B** |
| LookupSuccess | Dictionary |     NarrowRow |     1,994.79 ns |     38.044 ns |     35.587 ns |     1,979.37 ns |      - |     - |     - |         - |
| LookupFailure | Dictionary |     NarrowRow |     1,829.00 ns |     35.890 ns |     41.332 ns |     1,830.14 ns |      - |     - |     - |         - |
|        **Create** | **Dictionary** |       **WideRow** |     **1,699.98 ns** |      **7.211 ns** |      **6.022 ns** |     **1,701.19 ns** | **0.0687** |     **-** |     **-** |    **4616 B** |
| LookupSuccess | Dictionary |       WideRow |    81,528.16 ns |  1,597.573 ns |  1,901.797 ns |    80,558.87 ns |      - |     - |     - |         - |
| LookupFailure | Dictionary |       WideRow |    66,576.52 ns |    764.751 ns |    638.601 ns |    66,804.09 ns |      - |     - |     - |         - |
|        **Create** | **NameLookup** | **CommonEnglish** |    **24,167.59 ns** |    **368.611 ns** |    **344.799 ns** |    **24,194.60 ns** | **0.1831** |     **-** |     **-** |   **13032 B** |
| LookupSuccess | NameLookup | CommonEnglish |   667,714.71 ns |  6,291.882 ns |  5,885.430 ns |   666,565.16 ns |      - |     - |     - |       1 B |
| LookupFailure | NameLookup | CommonEnglish |   504,400.17 ns |  3,270.216 ns |  3,058.962 ns |   505,094.26 ns |      - |     - |     - |      10 B |
|        **Create** | **NameLookup** |     **NarrowRow** |       **451.91 ns** |      **8.784 ns** |      **8.216 ns** |       **447.71 ns** | **0.0119** |     **-** |     **-** |     **816 B** |
| LookupSuccess | NameLookup |     NarrowRow |     1,333.41 ns |     16.337 ns |     14.482 ns |     1,325.74 ns |      - |     - |     - |         - |
| LookupFailure | NameLookup |     NarrowRow |       840.79 ns |      2.911 ns |      2.431 ns |       840.68 ns |      - |     - |     - |         - |
|        **Create** | **NameLookup** |       **WideRow** |     **8,368.54 ns** |    **105.830 ns** |     **93.816 ns** |     **8,362.93 ns** | **0.0916** |     **-** |     **-** |    **6680 B** |
| LookupSuccess | NameLookup |       WideRow |   170,951.57 ns |  1,941.183 ns |  1,815.784 ns |   171,550.74 ns |      - |     - |     - |         - |
| LookupFailure | NameLookup |       WideRow |   116,561.27 ns |    507.366 ns |    449.766 ns |   116,485.27 ns |      - |     - |     - |         - |