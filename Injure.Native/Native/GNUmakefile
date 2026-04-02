RID       ?= $(error RID must be set)

ROOT     := .
SRCDIR   := $(ROOT)/src
OBJDIR   := $(ROOT)/obj/$(RID)
OUTDIR   := $(ROOT)/out/$(RID)

UNIBREAK     := $(ROOT)/extern/libunibreak
UNIBREAK_SRC := \
	$(UNIBREAK)/src/unibreakbase.c \
	$(UNIBREAK)/src/unibreakdef.c \
	$(UNIBREAK)/src/linebreak.c \
	$(UNIBREAK)/src/linebreakdata.c \
	$(UNIBREAK)/src/linebreakdef.c \
	$(UNIBREAK)/src/eastasianwidthdef.c
UNIBREAK_OBJ := $(patsubst $(UNIBREAK)/src/%.c,$(OBJDIR)/libunibreak/%.o,$(UNIBREAK_SRC))
UNIBREAK_DEP := $(UNIBREAK_OBJ:.o=.d)

SRC       := $(wildcard $(SRCDIR)/*.c)
OBJ       := $(patsubst $(SRCDIR)/%.c,$(OBJDIR)/%.o,$(SRC))
DEP       := $(OBJ:.o=.d)

CC        ?= cc
CPPFLAGS  ?=
CFLAGS    ?= -std=c11 -O2
LDFLAGS   ?=
LDLIBS    ?=

ifneq ($(filter osx-x64 osx-arm64,$(RID)),)
	OUT := $(OUTDIR)/libinjurenative.dylib
	CPPFLAGS += -DINJURENATIVE_MACOS
	CFLAGS += -fPIC
	LDFLAGS += -dynamiclib
else ifneq ($(filter linux-x64 linux-arm64,$(RID)),)
	OUT := $(OUTDIR)/libinjurenative.so
	CPPFLAGS += -DINJURENATIVE_POSIX -D_POSIX_C_SOURCE=200112L
	CFLAGS += -fPIC
	LDFLAGS += -shared
else
	$(error unsupported RID $(RID))
endif

.PHONY: all clean
all: $(OUT)

$(OUT): $(UNIBREAK_OBJ) $(OBJ)
	mkdir -p $(dir $@)
	$(CC) $(UNIBREAK_OBJ) $(OBJ) $(LDFLAGS) -o $@ $(LDLIBS)

$(OBJDIR)/libunibreak/%.o: $(UNIBREAK)/src/%.c
	mkdir -p $(dir $@)
	$(CC) $(CPPFLAGS) $(CFLAGS) -MMD -MP -MF $(@:.o=.d) -MT $@ -c -o $@ $<

$(OBJDIR)/%.o: $(SRCDIR)/%.c
	mkdir -p $(dir $@)
	$(CC) $(CPPFLAGS) $(CFLAGS) -MMD -MP -MF $(@:.o=.d) -MT $@ -c -o $@ $<

clean:
	rm -rf $(OBJDIR) $(OUTDIR)

-include $(UNIBREAK_DEP) $(DEP)
