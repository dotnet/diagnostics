//===-- SwiftMangledNameVisitor.h -------------------------------*- C++ -*-===//
//
// This source file is part of the Swift.org open source project
//
// Copyright (c) 2014 - 2016 Apple Inc. and the Swift project authors
// Licensed under Apache License v2.0 with Runtime Library Exception
//
// See https://swift.org/LICENSE.txt for license information
// See https://swift.org/CONTRIBUTORS.txt for the list of Swift project authors
//
//===----------------------------------------------------------------------===//

#ifndef liblldb_SwiftMangledNameVisitor_h_
#define liblldb_SwiftMangledNameVisitor_h_

#include "swift/Demangling/Demangle.h"

namespace lldb_private {
class SwiftMangledNameVisitor {
public:
#define NODE(e)                                                                \
  virtual void visit##e(swift::Demangle::NodePointer pointer);                 \
  void accept##e(swift::Demangle::NodePointer cur_node);
#include "swift/Demangling/DemangleNodes.def"

  virtual ~SwiftMangledNameVisitor() {}

  void visit(const char *mangled_name);

private:
  void accept(swift::Demangle::NodePointer pointer);
};
}

#endif // #ifndef liblldb_SwiftMangledNameVisitor_h_
