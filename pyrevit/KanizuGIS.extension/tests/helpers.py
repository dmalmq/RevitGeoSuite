# -*- coding: utf-8 -*-
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.join(os.path.dirname(HERE), "lib")
if LIB not in sys.path:
    sys.path.insert(0, LIB)

from kanizu_gis import snapshot, standards  # noqa: E402

REPO_STANDARDS = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(HERE))), "standards")


def load_standard():
    return standards.load_standard(REPO_STANDARDS)


def bound_as_standard(standard, skip=()):
    return [snapshot.BoundParameter(parameter.name, parameter.guid, parameter.binding, parameter.categories)
            for parameter in standard.parameters if parameter.name not in skip]


def materials_as_standard(standard):
    return [snapshot.MaterialInfo(100 + index, material.name, material.fill)
            for index, material in enumerate(standard.materials)]


def floor_types_as_standard(standard):
    return [snapshot.FloorTypeInfo(200 + index, floor_type.name, floor_type.gis_category,
                                   floor_type.gis_restriction, floor_type.material, [300 + index])
            for index, floor_type in enumerate(standard.floor_types)]


def model_on_standard(standard, **overrides):
    values = dict(
        bound_parameters=bound_as_standard(standard),
        floor_types=floor_types_as_standard(standard),
        materials=materials_as_standard(standard),
        levels=[snapshot.LevelInfo(1, u"1FL", 1, u"1F")],
        standard_version=str(standard.version))
    values.update(overrides)
    return snapshot.ModelSnapshot(**values)
