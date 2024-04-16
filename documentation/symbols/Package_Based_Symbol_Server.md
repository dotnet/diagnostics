# Zip Package Based Symbol Server #

A zip package based symbol server is a network service that implements the [Simple Symbol Query Protocol](Simple_Symbol_Query_Protocol.md) (SSQP) using a set of zip compressed files with specific contents to define the files that are available for download and the clientKey/filenames that address them. This specification defines the format of the zip packages. Although this format is intended to be fully compatible with NuGet package and NuGet symbol package specifications, the zip packages are not required to qualify under either standard.

## The zip package format ##

Each symbol package is a compressed container of files in the zip format. At the root of the container there must be one file named 'symbol\_index.json'. There may be an arbitrary number of other files in the container either at the root level or in arbitrarily nested sub-containers. The symbol\_index.json is a json array where each element identifies a clientKey and a blobPath,  the corresponding file in the zip that should be returned by an SSQP request for the clientKey. The blobPath is a filename, preceded by 0 or more container names using '/' as the separator:

```json
[
    {
        "clientKey" : "12387532",
        "blobPath" : "debug_info.txt"
    },
    {
        "clientKey" : "MyProgram.exe/09safnf82asddasdqwd998vds/MyProgram.exe",
        "blobPath" : "MyProgram.exe"
    },
    {
        "clientKey" : "12-09",
        "blobPath" : "Content/localized/en-us/data.xml"
    },
    {  
        "clientKey" : "23456",
        "blobPath" : "Content/localized/en-us/data.xml"
    }
]
```

## Expected service behavior ##

In order to implement the [Simple Symbol Query Protocol](Simple_Symbol_Query_Protocol.md) the service must identify a map entry in some package's symbol\_index.json which has the matching clientKey and then return the file pointed to by blobPath. If there is more than one entry in the same package which has the same clientKey value that is a bad package and the service may handle the error in an implementation specific way. If more than one package defines an entry with the same clientKey the service may choose one of the entries arbitrarily using implementation specific behavior. If the clientKey isn't present in any package the service may return a 404, or it may fallback to other implementation specific techniques to satisfy the request. The service must be prepared to handle having N different clientKeys all refer to the same blob.


## Combining with other sources of clientKeys ##

It is possible to run an SSQP service that uses more than one data source to determine the total set of clientKey/filename requests it is able to respond to. For example most existing NuGet symbol service implementations compute their own mappings for files in specific portions of a NuGet symbol package if the files are one of a few well-known formats. This specification explicitly allows for these other data sources to be integrated but implementers should document what happens in the event two disparate sources of mapping information request different blobs to be returned for the same clientKey.

## Usage notes ##

SSQP and the package based symbol server are best suited for controlled settings in which all publishers agree on the same conventions for publishing content. For example a set of developers on a particular project or the employees of a small company.

If there is disagreement (or outright malicious actors) these services do not intrinsically provide any way to determine who deserves to be more trusted. Publishers could easily submit packages with conflicting indexing information which will give undefined results to the SSQP clients. Running this service for a large group, such as worldwide unrestricted publishing access, is therefore not recommended without adding additional arbitration procedures.